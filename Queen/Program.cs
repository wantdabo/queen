using Queen.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var engine = Engine.CreateGameEngine();
            
            Console.ReadKey();
        }
    }
}
