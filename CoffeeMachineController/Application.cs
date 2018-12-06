using System;
using Microsoft.SPOT;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using Microsoft.SPOT.Hardware;
using SecretLabs.NETMF.Hardware.Netduino;
using Microsoft.SPOT.Net.NetworkInformation;
using Maple;

namespace CoffeeMachineController
{
    public enum CoffeeMachineState
    {
        None,
        Standby,
        TimedForActivation,
        Brewing
    }
    public enum TurnOffMode
    {
        Manual,
        Automatic
    }

    public class Application
    {
        /// <summary>
        /// Default value in milliseconds to automatically turn off the coffee machine.
        /// </summary>
        private const int TURN_OFF_MS_DEFAULT = 1800000;

        private NetworkInterface[] _networkInterfaces;
        private Timer TurnOffCoffeeTimer { get; set; }
        private AutoResetEvent TurnOffCoffeeTimerAutoEvent { get; set; }
        private Timer TurnOnCoffeeMachineTimer { get; set; }
        private AutoResetEvent TurnOnCoffeeMachineTimerAutoEvent { get; set; }

        public bool IsRunning { get; private set; }
        public DateTime MachineRunningSince { get; } = DateTime.Now;
        public DateTime BrewingCoffeeAt { get; private set; }
        public DateTime CurrentMachineStateSince { get; private set; }
        public CoffeeMachineState MachineState { get; set; }
        public TurnOffMode TurnOffMode { get; set; }

        public static Application Instance { get; private set; }

        /// <summary>
        /// Use <see cref="StartController"/> to create a new application instance.
        /// You can access the instance through the static <seealso cref="Instance"/> property after that.
        /// </summary>
        private Application() { }

        /// <summary>
        /// Create a new application instance and start running the maple server.
        /// </summary>
        public static void StartController()
        {
            Instance = new Application();
            Instance.IsRunning = true;

            Instance.MachineState = CoffeeMachineState.Standby;
            Instance.TurnOffMode = TurnOffMode.Automatic;
            Instance.CurrentMachineStateSince = DateTime.Now;

            Instance.Initialize();
        }

        public void Initialize()
        {
            bool goodToGo = false;

            try
            {
                goodToGo = InitializeNetwork();
            }
            catch (Exception e)
            {
                Debug.Print(e.Message);
            }

            if (goodToGo)
            {
                Debug.Print("Network done.");

                //Setup local time of the card
                

                // start web server
                MapleServer server = new MapleServer();
                server.Start();
            }
        }


        /// <summary>
        /// Get the status of the coffee maker
        /// </summary>
        /// <param name="clientSocket"></param>
        public void RequestCoffeeMachineStatus()
        {
            
        }


        /// <summary>
        /// Request to turn the coffee machine on.
        /// </summary>
        /// <param name="turnOffMode"></param>
        /// <param name="customMsToTurnOff">A custom time in milliseconds to turn of the machine
        /// if automatic turn off mode is switched on.</param>
        /// <param name="delayBrewingInMs">The delay in milliseconds, befor turning the coffee machine on.</param>
        public void RequestTurnOnCoffeeMachine(TurnOffMode turnOffMode = TurnOffMode.Automatic,
            int customMsToTurnOff = 0, int delayBrewingInMs = 0)
        {
            TurnOffMode = turnOffMode;

            // Delay the brewing if requested
            if (delayBrewingInMs > 0)
            {
                this.MachineState = CoffeeMachineState.TimedForActivation;

                BrewingCoffeeAt = DateTime.Now.AddSeconds(delayBrewingInMs);

                // Create an AutoResetEvent to signal the timeout threshold in the
                // timer callback has been reached.
                TurnOnCoffeeMachineTimerAutoEvent = new AutoResetEvent(false);

                TimeSpan dueTime = new TimeSpan(0, 0, 0, 0, delayBrewingInMs);

                TurnOnCoffeeMachineTimer = new Timer((obj) => { TurnOnCoffeeMachine(customMsToTurnOff); }, 
                    TurnOnCoffeeMachineTimerAutoEvent, dueTime, dueTime);

                // When autoEvent signals, dispose of the timer.
                TurnOnCoffeeMachineTimerAutoEvent.WaitOne();
                TurnOnCoffeeMachineTimer.Dispose();
            }
            else
            {
                TurnOnCoffeeMachine(customMsToTurnOff);
            }
        }


        /// <summary>
        /// Request to turn the coffee machine off and all delayed processes.
        /// </summary>
        public void RequestTurnOffCoffeeMachine()
        {
            // Release and stop the delayed brewing
            if (TurnOnCoffeeMachineTimerAutoEvent != null)
                TurnOnCoffeeMachineTimerAutoEvent.Set();

            BrewingCoffeeAt = DateTime.MinValue;

            // Release and stop automatic deactivation timer
            if (TurnOffCoffeeTimerAutoEvent != null)
                TurnOffCoffeeTimerAutoEvent.Set();

            // Finally turn the machine off
            TurnOffCoffeeMachine();
        }
        

        /// <summary>
        /// Turn the coffee machine on.
        /// </summary>
        /// <param name="customMsToTurnOff">A custom time in milliseconds to turn of the machine
        /// if automatic turn off mode is switched on.</param>
        private void TurnOnCoffeeMachine(int customMsToTurnOff = 0)
        {
            this.MachineState = CoffeeMachineState.Brewing;

            Ports.Led.Write(true);
            Ports.Relay.Write(false);

            // Setup automatic turn off if requested by the client.
            if (TurnOffMode == TurnOffMode.Automatic)
            {
                // Create an AutoResetEvent to signal the timeout threshold in the
                // timer callback has been reached.
                TurnOffCoffeeTimerAutoEvent = new AutoResetEvent(false);

                TimeSpan dueTime = new TimeSpan(0, 0, 0, 0, customMsToTurnOff <= 0 ? TURN_OFF_MS_DEFAULT : customMsToTurnOff);
                
                TurnOffCoffeeTimer = new Timer((obj) => { TurnOffCoffeeMachine(); }, TurnOffCoffeeTimerAutoEvent, dueTime, dueTime);

                // When autoEvent signals, dispose of the timer.
                TurnOffCoffeeTimerAutoEvent.WaitOne();
                TurnOffCoffeeTimer.Dispose();
            }
        }


        /// <summary>
        /// Turn coffee machine off.
        /// </summary>
        private void TurnOffCoffeeMachine()
        {
            this.MachineState = CoffeeMachineState.Standby;

            Ports.Led.Write(false);
            Ports.Relay.Write(true);
        }

        private bool InitializeNetwork()
        {
            if (SystemInfo.SystemID.SKU == 3)
            {
                Debug.Print("Wireless tests run only on Device");
                return false;
            }

            Debug.Print("Getting all the network interfaces.");
            _networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();

            // debug output
            ListNetworkInterfaces();

            // loop through each network interface
            foreach (var net in _networkInterfaces)
            {
                // debug out
                ListNetworkInfo(net);

                switch (net.NetworkInterfaceType)
                {
                    case (NetworkInterfaceType.Ethernet):
                        Debug.Print("Found Ethernet Interface");
                        break;
                    case (NetworkInterfaceType.Wireless80211):
                        Debug.Print("Found 802.11 WiFi Interface");
                        break;
                    case (NetworkInterfaceType.Unknown):
                        Debug.Print("Found Unknown Interface");
                        break;
                }

                // check for an IP address, try to get one if it's empty
                return CheckIPAddress(net);
            }

            // if we got here, should be false.
            return false;
        }
        private bool CheckIPAddress(NetworkInterface net)
        {
            int timeout = 10000; // timeout, in milliseconds to wait for an IP. 10,000 = 10 seconds

            // check to see if the IP address is empty (0.0.0.0). IPAddress.Any is 0.0.0.0.
            if (net.IPAddress == IPAddress.Any.ToString())
            {
                Debug.Print("No IP Address");

                if (net.IsDhcpEnabled)
                {
                    Debug.Print("DHCP is enabled, attempting to get an IP Address");

                    // ask for an IP address from DHCP [note this is a static, not sure which network interface it would act on]
                    int sleepInterval = 10;
                    int maxIntervalCount = timeout / sleepInterval;
                    int count = 0;
                    while (IPAddress.GetDefaultLocalAddress() == IPAddress.Any && count < maxIntervalCount)
                    {
                        Debug.Print("Sleep while obtaining an IP");
                        Thread.Sleep(10);
                        count++;
                    };

                    // if we got here, we either timed out or got an address, so let's find out.
                    if (net.IPAddress == IPAddress.Any.ToString())
                    {
                        Debug.Print("Failed to get an IP Address in the alotted time.");
                        return false;
                    }

                    Debug.Print("Got IP Address: " + net.IPAddress.ToString());
                    return true;

                    //NOTE: this does not work, even though it's on the actual network device. [shrug]
                    // try to renew the DHCP lease and get a new IP Address
                    //net.RenewDhcpLease ();
                    //while (net.IPAddress == "0.0.0.0") {
                    //    Thread.Sleep (10);
                    //}

                }
                else
                {
                    Debug.Print("DHCP is not enabled, and no IP address is configured, bailing out.");
                    return false;
                }
            }
            else
            {
                Debug.Print("Already had IP Address: " + net.IPAddress.ToString());
                return true;
            }

        }
        private void ListNetworkInterfaces()
        {
            foreach (var net in _networkInterfaces)
            {
                switch (net.NetworkInterfaceType)
                {
                    case (NetworkInterfaceType.Ethernet):
                        Debug.Print("Found Ethernet Interface");
                        break;
                    case (NetworkInterfaceType.Wireless80211):
                        Debug.Print("Found 802.11 WiFi Interface");
                        break;
                    case (NetworkInterfaceType.Unknown):
                        Debug.Print("Found Unknown Interface");
                        break;
                }
            }
        }
        private void ListNetworkInfo(NetworkInterface net)
        {
            try
            {
                Debug.Print("MAC Address: " + BytesToHexString(net.PhysicalAddress));
                Debug.Print("DHCP enabled: " + net.IsDhcpEnabled.ToString());
                Debug.Print("Dynamic DNS enabled: " + net.IsDynamicDnsEnabled.ToString());
                Debug.Print("IP Address: " + net.IPAddress.ToString());
                Debug.Print("Subnet Mask: " + net.SubnetMask.ToString());
                Debug.Print("Gateway: " + net.GatewayAddress.ToString());

                if (net is Wireless80211)
                {
                    var wifi = net as Wireless80211;
                    Debug.Print("SSID:" + wifi.Ssid.ToString());
                }
            }
            catch (Exception e)
            {
                Debug.Print("ListNetworkInfo exception:  " + e.Message);
            }

        }
        private string BytesToHexString(byte[] bytes)
        {
            string hexString = string.Empty;

            // Create a character array for hexadecimal conversion.
            const string hexChars = "0123456789ABCDEF";

            // Loop through the bytes.
            for (byte b = 0; b < bytes.Length; b++)
            {
                if (b > 0)
                    hexString += "-";

                // Grab the top 4 bits and append the hex equivalent to the return string.        
                hexString += hexChars[bytes[b] >> 4];

                // Mask off the upper 4 bits to get the rest of it.
                hexString += hexChars[bytes[b] & 0x0F];
            }

            return hexString;
        }
    }
}
