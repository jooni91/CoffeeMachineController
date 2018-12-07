using System;

namespace CoffeeMachineController
{
    public class CoffeeMachineStatus
    {
        public DateTime uptime { get; set; }
        public DateTime lastchangedstate { get; set; }
        public DateTime startingbrewat { get; set; }
        public DateTime turningoffat { get; set; }
        public string state { get; set; }
        public string mode { get; set; }
    }
}
