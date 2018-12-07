using Maple;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;
using Microsoft.SPOT.Net.NetworkInformation;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

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
        private const int TURN_OFF_MIN_DEFAULT = 45;
        private const string NTP_POOL_URL = "fi.pool.ntp.org";
        private const int NTP_OFFSET = 2;

        private NetworkInterface[] _networkInterfaces;
        private CoffeeMachineState machineState = CoffeeMachineState.None;
        private Timer TurnOffCoffeeTimer { get; set; }
        private Timer TurnOnCoffeeMachineTimer { get; set; }

        public bool IsRunning { get; private set; }
        public DateTime MachineRunningSince { get; private set; }
        public DateTime BrewingCoffeeAt { get; private set; }
        public DateTime TurningMachineOffAt { get; set; }
        public DateTime CurrentMachineStateSince { get; private set; }
        public CoffeeMachineState MachineState
        {
            get { return machineState; }
            set { machineState = value; CurrentMachineStateSince = DateTime.Now; }
        }
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
                Utility.SetLocalTime(NTPTime(NTP_POOL_URL, NTP_OFFSET));
                Debug.Print("Local time was set. Current local time is " + DateTime.Now);

                //Setup initial time variables
                MachineRunningSince = DateTime.Now;

                MachineState = CoffeeMachineState.Standby;
                TurnOffMode = TurnOffMode.Automatic;

                // start web server
                MapleServer server = new MapleServer();
                server.Start();
            }
        }


        /// <summary>
        /// Get the status of the coffee maker
        /// </summary>
        /// <param name="clientSocket"></param>
        public CoffeeMachineStatus RequestCoffeeMachineStatus()
        {
            var response = new CoffeeMachineStatus();
            response.uptime = MachineRunningSince;
            response.lastchangedstate = CurrentMachineStateSince;
            response.state = ConvertStateEnumToString(MachineState);
            response.mode = TurnOffMode == TurnOffMode.Automatic ? "automatic" : "manual";

            if (MachineState == CoffeeMachineState.TimedForActivation)
                response.startingbrewat = BrewingCoffeeAt;

            if (MachineState == CoffeeMachineState.Brewing && TurnOffMode == TurnOffMode.Automatic)
                response.turningoffat = TurningMachineOffAt;

            return response;
        }


        /// <summary>
        /// Request to turn the coffee machine on.
        /// </summary>
        /// <param name="turnOffMode"></param>
        /// <param name="customMsToTurnOff">A custom time in milliseconds to turn of the machine
        /// if automatic turn off mode is switched on.</param>
        /// <param name="delayBrewingByMinutes">The delay in milliseconds, befor turning the coffee machine on.</param>
        public void RequestTurnOnCoffeeMachine(TurnOffMode turnOffMode = TurnOffMode.Automatic,
            int customMinutesToTurnOff = 0, int delayBrewingByMinutes = 0)
        {
            TurnOffMode = turnOffMode;

            // Delay the brewing if requested and the machine was not already in brewing mode
            if (delayBrewingByMinutes > 0 && MachineState != CoffeeMachineState.Brewing)
            {
                // Check if request is overriding previous request and dispose
                // previous turn on timer in that case.
                TurnOnCoffeeMachineTimer?.Dispose();

                this.MachineState = CoffeeMachineState.TimedForActivation;

                BrewingCoffeeAt = DateTime.Now.AddMinutes(delayBrewingByMinutes);

                TimeSpan dueTime = new TimeSpan(0, delayBrewingByMinutes, 0);

                TurnOnCoffeeMachineTimer = new Timer((obj) => { TurnOnCoffeeMachine(customMinutesToTurnOff); }, 
                    null, dueTime, dueTime);
            }
            else
            {
                // Check if request is overriding previous request and dispose
                // previous turn on timer in that case.
                TurnOnCoffeeMachineTimer?.Dispose();

                TurnOnCoffeeMachine(customMinutesToTurnOff);
            }
        }


        /// <summary>
        /// Request to turn the coffee machine off and all delayed processes.
        /// </summary>
        public void RequestTurnOffCoffeeMachine()
        {
            // Release and stop the delayed brewing
            TurnOnCoffeeMachineTimer?.Dispose();

            BrewingCoffeeAt = DateTime.MinValue;

            // Finally turn the machine off
            TurnOffCoffeeMachine();
        }


        public void RequestChangeBrewingDelay(int minutes)
        {
            if (MachineState == CoffeeMachineState.TimedForActivation)
            {
                var delay = BrewingCoffeeAt.AddMinutes(minutes);
                TimeSpan dueDate;

                if (delay < DateTime.Now)
                    dueDate = new TimeSpan(0, 0, 0, 0, 1);
                else
                {
                    BrewingCoffeeAt = delay;

                    dueDate = delay - DateTime.Now;
                }

                TurnOnCoffeeMachineTimer.Change(dueDate, dueDate.Add(new TimeSpan(1,0,0)));
            }
        }
        

        /// <summary>
        /// Turn the coffee machine on.
        /// </summary>
        /// <param name="customMinutesToTurnOff">A custom time in milliseconds to turn of the machine
        /// if automatic turn off mode is switched on.</param>
        private void TurnOnCoffeeMachine(int customMinutesToTurnOff = 0)
        {
            // If delay timer was running, dispose it at this point
            TurnOnCoffeeMachineTimer?.Dispose();

            this.MachineState = CoffeeMachineState.Brewing;

            Ports.Led.Write(true);
            Ports.Relay.Write(false);

            // Setup automatic turn off if requested by the client.
            if (TurnOffMode == TurnOffMode.Automatic)
            {
                // Check if request is overriding previous request and dispose
                // previous turn off timer in that case.
                TurnOffCoffeeTimer?.Dispose();

                TimeSpan dueTime = new TimeSpan(0, customMinutesToTurnOff <= 0 ? TURN_OFF_MIN_DEFAULT : customMinutesToTurnOff, 0);
                TurningMachineOffAt = DateTime.Now.AddTicks(dueTime.Ticks);
                
                TurnOffCoffeeTimer = new Timer((obj) => { TurnOffCoffeeMachine(); }, null, dueTime, dueTime);
            }
            else
            {
                // Check if request is overriding previous request and dispose
                // previous turn off timer in that case.
                TurnOffCoffeeTimer?.Dispose();
            }
        }


        /// <summary>
        /// Turn coffee machine off.
        /// </summary>
        private void TurnOffCoffeeMachine()
        {
            // If automatic turn off timer was running, dispose it at this point
            TurnOffCoffeeTimer?.Dispose();

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

        /// <summary>
        /// Get the current time from a network time prtocol server.
        /// </summary>
        /// <param name="timeServer">Server url,</param>
        /// <param name="UTC_offset">The offset that should be applied to the time received from the ntp pool.</param>
        /// <returns></returns>
        private DateTime NTPTime(string timeServer, int UTC_offset)
        {
            // Find endpoint for timeserver
            IPEndPoint ep = new IPEndPoint(Dns.GetHostEntry(timeServer).AddressList[0], 123);

            // Connect to timeserver
            Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Connect(ep);

            // Make send/receive buffer
            byte[] ntpData = new byte[48];
            Array.Clear(ntpData, 0, 48);

            // Set protocol version
            ntpData[0] = 0x1B;

            // Send Request
            s.Send(ntpData);

            // Receive Time
            s.Receive(ntpData);

            byte offsetTransmitTime = 40;

            ulong intpart = 0;
            ulong fractpart = 0;

            for (int i = 0; i <= 3; i++)
                intpart = (intpart << 8) | ntpData[offsetTransmitTime + i];

            for (int i = 4; i <= 7; i++)
                fractpart = (fractpart << 8) | ntpData[offsetTransmitTime + i];

            ulong milliseconds = (intpart * 1000 + (fractpart * 1000) / 0x100000000L);

            s.Close();

            TimeSpan timeSpan = TimeSpan.FromTicks((long)milliseconds * TimeSpan.TicksPerMillisecond);
            DateTime dateTime = new DateTime(1900, 1, 1);
            dateTime += timeSpan;

            TimeSpan offsetAmount = new TimeSpan(0, UTC_offset, 0, 0, 0);
            DateTime networkDateTime = (dateTime + offsetAmount);

            return networkDateTime;
        }

        private string ConvertStateEnumToString(CoffeeMachineState state)
        {
            switch (state)
            {
                case CoffeeMachineState.Brewing:
                    return "brewing";
                case CoffeeMachineState.Standby:
                    return "standby";
                case CoffeeMachineState.TimedForActivation:
                    return "brewingsoon";
                default:
                    return "none";
            }
        }
    }
}
