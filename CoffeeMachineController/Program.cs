using Microsoft.SPOT;
using System;
using System.Threading;

namespace CoffeeMachineController
{
    public class Program
    {
        public static void Main()
        {
            Application.StartController();

            while (Application.Instance.IsRunning)
            {
                Thread.Sleep(10000);
                Debug.Print("Still alive: " + DateTime.Now);
            }
        }

    }
}
