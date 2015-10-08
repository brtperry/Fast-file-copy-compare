using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;
using System.IO;
using System.Threading;

namespace WFile
{
    interface IException
    {
        event Action<Exception> OnError;
    }

    interface ICopy
    {
        event Action<long, long> OnBytesCopied;

        event Action<string> OnBegincopy;

        Task Start(List<Item> items);
    }

    interface ICompared
    {
        event Action<List<Item>> OnCompared;

        void Match();
    }

    public abstract class Cancellation
    {
        public event Action<bool> CancellationPending;

        private readonly CancellationTokenSource cancellationToken;

        public Cancellation()
        {
            cancellationToken = new CancellationTokenSource();
        }

        public void Cancel(object sender, EventArgs e)
        {
            cancellationToken.Cancel();

            CancellationPending(true);
        }
    }

    #region Copy

        public class Copy : Cancellation, ICopy, IException
        {
            public event Action<Exception> OnError;

            public event Action<long, long> OnBytesCopied;

            public event Action<string> OnBegincopy;

            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
            [return: MarshalAs(UnmanagedType.Bool)]
            static extern bool CopyFileEx(string lpExistingFileName, string lpNewFileName, CopyProgressRoutine lpProgressRoutine, IntPtr lpData, ref Int32 pbCancel, CopyFileFlags dwCopyFlags);

            delegate CopyProgressResult CopyProgressRoutine(long totalFileSize, long totalBytesTransferred, long streamSize, long streamBytesTransferred, uint dwStreamNumber, CopyProgressCallbackReason dwCallbackReason, IntPtr hSourceFile, IntPtr hDestinationFile, IntPtr lpData);

            private int cancelPending;

            private readonly string destination;

            enum CopyProgressResult : uint
            {
                //Continue the copy operation
                ProgressContinue = 0,
            }

            enum CopyProgressCallbackReason : uint
            {
                CallbackChunkFinished = 0x00000000,
                CallbackStreamSwitch = 0x00000001
            }

            [Flags]
            enum CopyFileFlags : uint
            {
                CopyFileRestartable = 0x00000002,
                CopyFileAllowDecryptedDestination = 0x00000008
            }

            public Copy(string to)
            {
                CancellationPending += CancellationPendingReceived;

                destination = to;
            }

            private void CancellationPendingReceived(bool value)
            {
                cancelPending = -1;
            }

            public virtual async Task Start(List<Item> items)
            {
                //Parallel.ForEach(items, item =>
                //    {
                //        Console.WriteLine(item.Name);
                //    });

                await Task.Run(() => {

                    foreach (var item in items)
                    {
                        if (cancelPending == -1)
                        {
                            ExceptionHappended(new Exception("Action cancelled by operator."));

                            return;
                        }

                        if (string.IsNullOrWhiteSpace(item.Destination))
                        {
                            item.Destination = destination;
                        }
                        else
                        {
                            var sub = destination + item.Destination;

                            if (!Directory.Exists(sub))
                            {
                                Directory.CreateDirectory(sub);
                            }

                            item.Destination = sub;
                        }

                        item.Destination += item.Name;

                        try
                        {
                            if (OnBegincopy != null)
                            {
                                OnBegincopy(item.Name);
                            }

                            Parallel.Invoke(() => WCopy(item));
                        }
                        catch (Exception ex)
                        {
                            ExceptionHappended(ex);
                        }
                    }
                });
            }

            private void ExceptionHappended(Exception ex)
            {
                if (OnError != null) OnError(ex);
            }

            private void WCopy(Item item)
            {
                CopyFileEx(item.Source, item.Destination, CopyProgressHandler, IntPtr.Zero, ref cancelPending, CopyFileFlags.CopyFileRestartable | CopyFileFlags.CopyFileAllowDecryptedDestination);
            }

            private CopyProgressResult CopyProgressHandler(long total, long transferred, long streamSize, long streamByteTrans, uint dwStreamNumber, CopyProgressCallbackReason reason, IntPtr hSourceFile, IntPtr hDestinationFile, IntPtr lpData)
            {
                switch (reason)
                {
                    //case CopyProgressCallbackReason.CallbackStreamSwitch:

                    //    break;

                    case CopyProgressCallbackReason.CallbackChunkFinished:

                        if (OnBytesCopied != null)
                        {
                            OnBytesCopied(total, transferred);
                        }

                        break;
                }

                return CopyProgressResult.ProgressContinue;
            } 
        }

    #endregion

    #region Collector

        public class Collector : Cancellation, IException
        {
            public event Action<Exception> OnError;

            private int cancelPending;

            private string root;

            public Collector()
            {
                CancellationPending += CancellationPendingReceived;
            }

            public async virtual Task<IEnumerable<Item>> Collect(string folder)
            {
                root = folder;

                var results = new List<Item>();

                await Task.Run(() => Recurse(folder, ref results));

                return results;
            }

            private void CancellationPendingReceived(bool value)
            {
                cancelPending = -1;

                if (OnError != null) OnError(new Exception("Action cancelled by operator."));
            }

            #region WIN_32 : Recursive directory function

            /// <summary>
            /// Recursive function to populate a MyImage list.
            /// </summary>
            /// <param name="directory"></param>
            /// <param name="lmi"></param>
            private void Recurse(string directory, ref List<Item> lmi)
            {
                if (string.IsNullOrWhiteSpace(directory))
                    return;

                var invalidFileHandle = new IntPtr(-1);

                Win32FindData w32File;

                var hwnd = FindFirstFile(@directory + @"\*", out w32File);

                if (hwnd == invalidFileHandle) return;

                do
                {
                    if (cancelPending == -1)
                    {
                        CloseWindowHandle(hwnd);

                        return;
                    }

                    if ((w32File.dwFileAttributes & FileAttributes.Directory) != 0)
                    {
                        if (w32File.cFileName != "." && w32File.cFileName != "..")
                        {
                            var subdirectory = directory + (directory.EndsWith(@"\") ? "" : @"\") + w32File.cFileName;

                            Recurse(subdirectory, ref lmi);
                        }
                    }
                    else
                    {
                        if (w32File.cFileName != "Thumbs.db")
                        {
                            const string SLASH = @"\";

                            var sub = directory.Substring(root.Length);

                            var name = w32File.cFileName;

                            var itm = new Item(name)
                            {
                                Destination = sub != "" ? sub + SLASH : sub,

                                Source = directory + (sub != "" ? SLASH + name : name),

                                LastWriteTime = DateTime.FromFileTime((((long)w32File.ftLastWriteTime.dwHighDateTime) << 32) | ((uint)w32File.ftLastWriteTime.dwLowDateTime))
                            };

                            lmi.Add(itm);
                        }
                    }
                }
                while (FindNextFile(hwnd, out w32File));

                CloseWindowHandle(hwnd);
            }

            private void CloseWindowHandle(IntPtr hwnd)
            {
                FindClose(hwnd);
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            public struct Win32FindData
            {
                public readonly FileAttributes dwFileAttributes;
                private readonly FILETIME ftCreationTime;
                private readonly FILETIME ftLastAccessTime;
                public readonly FILETIME ftLastWriteTime;
                private readonly uint nFileSizeHigh; //changed all to uint from int, otherwise you run into unexpected overflow
                private readonly uint nFileSizeLow;  //| http://www.pinvoke.net/default.aspx/Structures/WIN32_FIND_DATA.html
                private readonly uint dwReserved0;   //|
                private readonly uint dwReserved1;   //v
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
                public readonly string cFileName;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
                private readonly string cAlternate;
            }

            [DllImport("kernel32", SetLastError = true)]
            static extern bool FindClose(IntPtr hndFindFile);

            [DllImport("kernel32", CharSet = CharSet.Unicode)]
            private static extern IntPtr FindFirstFile(string lpFileName, out Win32FindData lpFindFileData);

            [DllImport("kernel32", CharSet = CharSet.Unicode)]
            private static extern bool FindNextFile(IntPtr hFindFile, out Win32FindData lpFindFileData);

            #endregion
        }

    #endregion

    #region Comparer

        public class Compare : ICompared, IException 
        {
            public event Action<Exception> OnError;

            public event Action<List<Item>> OnCompared;

            private readonly IEnumerable<Item> source;

            private readonly IEnumerable<Item> destination;

            public Compare(IEnumerable<Item> src, IEnumerable<Item> dest)
            {
                source = src;

                destination = dest;
            }

            public async virtual void Match()
            {
                try
                {
                    await Task.Run(() => OnCompared(source.Except(destination, new EqualityComparer()).ToList()));
                }
                catch (AggregateException ae)
                {
                    ae.Handle((x) => 
                    {
                        //
                        // It's likely that the only exception would be ArgumentNullException, this I
                        // can handle, anything else it's wierd.
                        // 
                        if (x is ArgumentNullException)
                        {
                            OnError(x);
                        }

                        return false;
                    });
                }
            }
        }

    #endregion

    #region Item

        public class Item
        {
            public string Name;

            public string Destination;

            public string Source;

            public DateTime LastWriteTime;

            public Item(string n)
            {
                Name = n;
            }

            public override string ToString()
            {
                return string.Format("Name: {0}, Source: {1}, Destination: {2}", Name, Source, Destination);
            }
        }

    #endregion

    #region EqualityComparer

        public class EqualityComparer : IEqualityComparer<Item>
        {
            public virtual bool Equals(Item x, Item y)
            {
                if (ReferenceEquals(x, y)) return true;

                return x != null && y != null && x.Name.Equals(y.Name) && x.LastWriteTime == y.LastWriteTime;
            }

            public int GetHashCode(Item obj)
            {
                var hash = obj.Name == null ? 0 : obj.Name.GetHashCode();

                var lwt = obj.LastWriteTime.GetHashCode();

                //Calculate the hash code for the item.  
                return hash ^ lwt;
            }
        }

    #endregion
}
