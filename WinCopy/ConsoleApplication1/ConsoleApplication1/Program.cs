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
            var wfc = new Collector();

            var src = wfc.Collect(b);

            var dest = wfc.Collect(a);

            var wcc = new Compare(src, dest);

            wcc.OnError += OnException;

            wcc.OnCompared += Compared;

            wcc.Match();


            while (!completed) { };


            Console.WriteLine(a);
        }

        private static void OnException(Exception ex)
        {
            Console.WriteLine(ex.Message);
        }

        private static void BytesCopied(int a, int b)
        {

        }

        private static void Progress(int step)
        {

        }

        private static void Compared(List<Item> items)
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
        }
    }
}
