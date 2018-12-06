using Microsoft.SPOT.Hardware;
using SecretLabs.NETMF.Hardware.Netduino;

namespace CoffeeMachineController
{
    static class Ports
    {
        public static OutputPort Led { get; } = new OutputPort(Pins.ONBOARD_LED, false);
        public static OutputPort Relay { get; } = new OutputPort(Pins.GPIO_PIN_D9, true);
    }
}
