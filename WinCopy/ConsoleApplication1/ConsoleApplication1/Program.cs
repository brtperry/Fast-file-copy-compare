using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WFile;

namespace ConsoleApplication1
{
    class Program
    {
        const string a = @"C:\Test\a\";
            
        const string b = @"C:\Test\b\";

        private static int numberOfFilesToCopy;

        static void Main(string[] args)
        {
            Run();

            ConsoleKeyInfo cki;

            // Prevent example from ending if CTL+C is pressed.
            Console.TreatControlCAsInput = true;

            do
            {
                cki = Console.ReadKey(false);
                
                if ((cki.Modifiers & ConsoleModifiers.Control) != 0)
                {
                    if (cki.Key != ConsoleKey.C)
                    {
                        continue;
                    }

                    Console.WriteLine("{0} (character '{1}')", cki.Key, cki.KeyChar);
                }

            } while (cki.Key != ConsoleKey.Escape);
        }

        private static async void Run()
        {
            var wfc = new Collector();

            var src = await wfc.Collect(b);

            var dest = await wfc.Collect(a);

            var wcc = new Compare(src, dest);

            wcc.OnError += OnException;

            wcc.OnCompared += Compared;

            wcc.Match();
        }

        private static void OnException(Exception ex)
        {
            Console.WriteLine(ex.Message);
        }

        private static void BytesCopied(long a, long b)
        {
            Console.WriteLine("Copied: {0} of {1}.", a, b);
        }

        private static void OnBegincopy(string n)
        {
            numberOfFilesToCopy -= 1;

            Console.WriteLine("File about to be copied {0}", n);
        }

        private static async void Compared(List<Item> items)
        {
            numberOfFilesToCopy = items.Count;

            Console.WriteLine("Number of files to copy {0}.", numberOfFilesToCopy);

            var wc = new Copy(a);

            wc.OnError += OnException;

            wc.OnBytesCopied += BytesCopied;

            wc.OnBegincopy += OnBegincopy;

            await wc.Start(items);

            Console.WriteLine("Remaining files {0}.", numberOfFilesToCopy);
        }
    }
}
