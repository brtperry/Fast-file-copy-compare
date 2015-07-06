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
        static bool completed = false;

        const string a = @"C:\Test\a\";
            
        const string b = @"C:\Test\b\";

        static void Main(string[] args)
        {
            Run();

            while (!completed) { };

            Console.ReadKey();
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

        private static void Progress(int step)
        {
            Console.WriteLine("Step" + step);
        }

        private static async void Compared(List<Item> items)
        {
            var wc = new Copy(a);

            wc.OnError += OnException;

            wc.OnBytesCopied += BytesCopied;

            wc.OnProgressStep += Progress;

            // After comparison copy the results
            foreach (var i in items)
            {
                Console.WriteLine(i.ToString());
            }

            await wc.PerformAction(items);

            completed = true;
        }
    }
}
