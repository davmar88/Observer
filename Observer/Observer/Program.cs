using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CsvHelper;
using Newtonsoft.Json;
using NLog;

namespace Observer
{
    /// <summary>
    /// Struct for Directory item.
    /// </summary>
    struct DirectoryItem
    {
        public Uri BaseUri;

        public string AbsolutePath
        {
            get
            {
                return string.Format("{0}/{1}", BaseUri, Name);
            }
        }

        public DateTime DateCreated;
        public bool IsDirectory;
        public string Name;
        public List<DirectoryItem> Items;
    }

    /// <summary>
    /// Main class.
    /// </summary>
    class MainClass
    {
        static readonly CancellationTokenSource tokenSource = new CancellationTokenSource();

        //network handset information
        public static string imsi, cellid,  lac, boxlongitude, boxlatitude, datetime, macaddr, country, brand, imsioperator, ue_id, networkid, phcellid, rsrq, finallat,finallon, installer_username, installer_password, boxprivateip, boxpublicip, boxuniqueid, token;
        public static string imei = "IMEI";
        public static double wavelength = 0.1609055; //Wavelenght for 1862600000 hertz
        public static double pathloss = 41.84239;
        public static double signaldecay = 2.7;
        public static double refdistance = 1;
        public static float distance;
        public static int seconds, fk_locreport, id_networkdetail, tac, freq, locreportid, rsrp, imsitableid; //hsspid, mmepid, ltepid;
        public static double txpower, rssi;
        static Logger logger = LogManager.GetLogger("Logs.txt");
        public static int mcc, mnc;


        //url.conf file
        public static string username, password, enbconf, mmeconf, myiappsurl,ftpurl, openaircnurl, openairinterfaceurl, observerurl, radiovalidationurl ;

        public static List<double> kilometerset = new List<double>();
        public static List<float> longitudeset = new List<float>();
        public static List<float> latitudeset = new List<float>();
        public static List<int> cellidset = new List<int>();
        //public static List<string> abspath = new List<string>();
        public static List<string> networklist = new List<string>();
        public static List<string> mmelist = new List<string>();
        public static List<string> mcclist = new List<string>();
        public static List<string> towerinfo = new List<string>();
        public static List<string> earfcnlist = new List<string>();
        public static string countrymccfile = "/home/marietjie/configfiles/countrymcc.txt";




        /// <summary>
        /// The entry point of the program, where the program control starts and ends.
        /// </summary>
        /// <param name="args">The command-line arguments.</param>
        public static void Main(string[] args)
        {
            Process currentProcess = Process.GetCurrentProcess();
            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
            string pid = currentProcess.Id.ToString();

            StreamWriter writer = new StreamWriter("/home/marietjie/configfiles/pid.txt", false);
            writer.WriteLine(pid);
            writer.Close();

            ReadUrlCOnfigFile();
            PopulateFileLists();
            //Reading appsettings.txt file contents and loading them into public variables used to pass them onto the httpclient connection
            seconds = ReadAppSettingsFile();

            //Programs the Nlog
            ProgramNLogger();


            //Retrieving the Mac Address for the device
            macaddr = GetMacAddress();

            if (macaddr == "")
            {
                macaddr = GetFirstMacAddress();
                logger.Trace("macaddr is empty, initiating second attempt to find mac address");

            }


            //kills any scripts that might interfere with a new program startup
           // KillHSSScript();
            //KillLTEMMEScripts();

            



            //starts HSS so long
            //StartHSS();

            //below will keep on rotating the networks and restart the MME and EnodeB
           /* new Thread(delegate ()
            {
                NetworkRotation(networklist, mmelist, mcclist,towerinfo,earfcnlist,countrymccfile);

            }).Start();*/


            //ongoing loop used when running the background service
            while (!tokenSource.Token.IsCancellationRequested)
            {

                //uploading the values retrieved from database and appsettings.txt file to Opaque
                boxprivateip = GetLocalIPAddress();
                boxpublicip = GetLocalIPAddress();
                OpaqueBoxLogin(boxprivateip, radiovalidationurl, installer_username, installer_password, boxlongitude, boxlatitude, boxuniqueid).GetAwaiter().GetResult();
                BoxLoginPepla(boxuniqueid).GetAwaiter().GetResult();

                calculateLocation();

                Thread.Sleep(70000);
            }
        }




        public static void calculateLocation()
        {
            string finallatlon = "";
            string calmcc = "";
            string calmnc = "";
            string calmno = "";

            List<TowerMeasurementReportCombined> towermeascombinedlist = new List<TowerMeasurementReportCombined>();


            //iterate through measurement report list where sent is = 0. Get the average based on rsrp
            //and obtain the cellid.
            try
            {
                using (var measurementreport = new ObserveContext())
                {
                    var measurementreportlist = measurementreport.Measurementreports
                               .Where(x => x.Sent == 0)
                       .GroupBy(
                           x =>
                           new
                           {
                               x.Cellid,
                               x.Imsi

                           })
                       .Select(x => new
                       {
                           x.Key.Cellid,
                           x.Key.Imsi,
                           Rsrpaverage = x.Average(p=>p.Rsrp)
                       });

                    //iterate through all items captured in measurementreportlist
                    foreach (var s in measurementreportlist)
                    {
                        try
                        {
                            //We dissect the imsi into mcc and mnc as this will be used as one of the criterias to get the location of each tower in TowerDetails
                            string mcc = s.Imsi.Substring(0, 3);
                            string mnc = s.Imsi.Substring(3, 3);
                            string newmncmcc = ReadMCCMNC(mcc + "" + mnc);
                            mnc = newmncmcc.Substring(3);
                            calmcc = mcc;
                            calmnc = mnc;

                            if(mnc.Substring(0,1)=="0")
                            {
                                mnc = mnc.Substring(1);
                                calmnc = mnc;
                            }
                            int t_mcc = int.Parse(mcc);
                            int t_mnc = int.Parse(mnc);
                            Console.WriteLine("MCC: {0} MNC: {1}",t_mcc,t_mnc);
                            int index = 0;

                            //getting the operator for the mcc and mnc
                            using (var observer = new ObserveContext())
                            {
                                var operatorlist = observer.Operators
                                    .Where(a => a.MCC == calmcc && a.MNC == calmnc) //s.Cellid builds the last part of the criteria to get the exact lat and lon
                                    .AsEnumerable();

                                //we know there will be only 1 item in this selection below, but we will take now this info from TowerDetails and combine it with MeasurementReports
                                //into 1 single list. 
                                foreach (var r in operatorlist)
                                {
                                    calmno = r.Network;
                           
                                }
                            }

                            //we going to now get the location from towerdetails of each cellid based on pci, mcc and mnc, and we are going to combine all these data into one list
                            using (var observer = new ObserveContext())
                            {
                                var towerlist = observer.TowerDetails
                                    .Where(a => a.Pci == s.Cellid && a.Mcc == t_mcc && a.Mnc == t_mnc) //s.Cellid builds the last part of the criteria to get the exact lat and lon
                                    .AsEnumerable();

                                //we know there will be only 1 item in this selection below, but we will take now this info from TowerDetails and combine it with MeasurementReports
                                //into 1 single list. 
                                foreach (var r in towerlist)
                                {
                                    towermeascombinedlist.Add(new TowerMeasurementReportCombined(s.Cellid,s.Rsrpaverage, r.Lat, r.Lon, s.Imsi,calmcc, calmnc, calmno,r.Tac));

                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Something went wrong reading TowerDetails: {0}", ex.Message);
                        }

                        //We will now attempt to update the current record of Measurementreports sent to be equal to 1
                        try
                        {
                            using (var updateMeasurementReport = new ObserveContext())
                            {
                                var stdMeasurementReport = updateMeasurementReport.Measurementreports.Where((c => c.Cellid == s.Cellid && c.Imsi == s.Imsi && c.Sent == 0)).ToList();
                                if (stdMeasurementReport != null)
                                {
                                    Console.WriteLine("MEASUREMENTREPORT SENT IS 0! GOING TO UPDATE to 1");
                                    stdMeasurementReport.ForEach(b => b.Sent = 1);
                                    updateMeasurementReport.SaveChanges();
                                }
                            }


                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("An error occurred while updating Measurementreports Table: " + ex.Message);
                            logger.Error(ex.Message, "An error occurred while updating Measurementreports Table ");
                        }
                    }

                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Something went wrong reading Measurementreports: {0}",ex.Message);
            }



            //Old code below:

            int countelements = towermeascombinedlist.Count;


            if(countelements==0)
            {
                Console.WriteLine("No measurement report yet, lets wait...");
            }

            if (countelements < 3 && countelements > 0)
            {
                Console.WriteLine("A measurement report extraction did occur with less than 2 celltowers, but we need to wait for more celltower reception, we cannot work out a location with less than 3 towers...");
            }

            if (countelements == 3 || countelements > 3)
            {
                Console.WriteLine("We have 3 or more towers in range, going to calculate the location of the phone...");
                try
                {
                    using (var context = new ObserveContext())
                    {
                        //
                        string variable = "@latlon";
                        string SQLQuery = String.Format("call CalculatePhoneLocation({0},{1},{2},{3},{4},{5},{6},{7},{8},{9});",
                             towermeascombinedlist[0].Lon.ToString(CultureInfo.InvariantCulture),
                         towermeascombinedlist[0].Lat.ToString(CultureInfo.InvariantCulture),
                         towermeascombinedlist[0].Rsrp.ToString(CultureInfo.InvariantCulture),
                         towermeascombinedlist[1].Lon.ToString(CultureInfo.InvariantCulture),
                         towermeascombinedlist[1].Lat.ToString(CultureInfo.InvariantCulture),
                         towermeascombinedlist[1].Rsrp.ToString(CultureInfo.InvariantCulture),
                         towermeascombinedlist[2].Lon.ToString(CultureInfo.InvariantCulture),
                         towermeascombinedlist[2].Lat.ToString(CultureInfo.InvariantCulture),
                         towermeascombinedlist[2].Rsrp.ToString(CultureInfo.InvariantCulture),
                         variable.ToString(CultureInfo.InvariantCulture));
                        Console.WriteLine(SQLQuery);

                        //int data = context.Database.ExecuteSqlCommand(SQLQuery);
                        //Console.WriteLine("data: {0}", data);

                        var data = context.Database.SqlQuery<string>(SQLQuery).Single();  //Throws error 
                        Console.WriteLine("data: {0}", data);
                        string tower = data.Split(':')[0];
                        Console.WriteLine("tower:{0}",tower);
                        string ln = data.Split(':')[1];
                        string lt = data.Split(':')[2];
                        Console.WriteLine("3rd Longitude: {0}", ln);
                        Console.WriteLine("3rd Latitude: {0}", lt);
                        finallatlon = lt + "," + ln;

                        Console.WriteLine("FINAL LOCATION: {0}", finallatlon);

                        if (finallatlon != null)
                        {
                            string mobilelat = finallatlon.Split(',')[0];
                            string mobilelon = finallatlon.Split(',')[1];

                            Console.WriteLine(mobilelat+" "+mobilelon);

                            string signalStrength = "signalstr";
                            string msisdn = "msisdn";
                            cellid = "cellid";

                           

                            UploadLocation(towermeascombinedlist[0].Imsi, imei, cellid, calmcc, calmnc, towermeascombinedlist[0].Lac.ToString(), calmno, boxlongitude, boxlatitude, mobilelon, mobilelat, macaddr, country, "LTE", towermeascombinedlist[0].Mno, "date", signalStrength, msisdn).GetAwaiter().GetResult();
                        }
                    }


                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error in Stored Procedure: {0} ", ex.Message);
                }
            }

        }












        public static void PopulateFileLists()
        {

            string[] enbfiles = Directory.GetFiles("/home/marietjie/configfiles/", "*enb.conf");
            foreach (string item in enbfiles)
            {
                Console.WriteLine("ADDING ENB DETAILS!!!!! {0}", item);
                networklist.Add(item);
            }

            string[] mmefiles = Directory.GetFiles("/home/marietjie/configfiles/", "*mme.conf");
            foreach (string item in mmefiles)
            {
                Console.WriteLine("ADDING MME DETAILS!!!!! {0}", item);
                mmelist.Add(item);
            }
            string[] towerfiles = Directory.GetFiles("/home/marietjie/configfiles/", "*towerinfo.txt");
            Console.WriteLine("COUNT TOWERINFODETAILS: {0}", towerfiles.Length);
            foreach (string item in towerfiles)
            {
                Console.WriteLine("ADDING TOWERINFO DETAILS!!!!! {0}",item);
                towerinfo.Add(item);
            }
            string[] earfcnfiles = Directory.GetFiles("/home/marietjie/configfiles/", "*earfcn.txt");
            Console.WriteLine("COUNT EARFCNFILES: {0}",earfcnfiles.Length);
            foreach (string item in earfcnfiles)
            {
                Console.WriteLine("ADDING EARFCN DETAILS!!!!! {0}", item);
                earfcnlist.Add(item);

            }

            if (File.Exists("/home/marietjie/configfiles/networkoperators.txt"))
            {
                string line = null;
                StreamReader reader = new StreamReader("/home/marietjie/configfiles/networkoperators.txt");
                // read all the lines in the file and store them in the List
                while ((line = reader.ReadLine()) != null)
                {
                    Console.WriteLine("ADDING MCCLIST (NETWORK DETAILS!!!!! {0}", line);
                    mcclist.Add(line);
                }
                reader.Close();
            }
            else
            {

                Console.WriteLine("CANNOT FIND ANY /home/marietjie/configfiles/networkoperators.txt FILE");

            }

        }













      




        public static async Task OpaqueBoxLogin(string ipaddress, string url, string username, string password,  string boxlon, string boxlat,  string boxid)
        {
            //username = installer
            //password = demo123
           
            try
            {
                using (var client = new HttpClient())
                {
                //https://www.myiapps.net/opaque/radiorec/radiorec.php?imsi=%20655020408441923&tmsi=%27%27&msisdn=%27%27&mno=001&imei=abc123qwe&cellid=123456&mcc=010&mnc=123&lac=82345&signalStrength=00.123&lng=-28.7230&lat=-23.9982&locatorBoxID=client-1
                    //locationBoxID=client-0&ipAddress=100.122.123:333&latitude=22.123456&longitude=0.123456&remoteAccessID=12345&remoteAccessPsw=password123
                    var queryParamater = string.Format(url, boxid, ipaddress, boxlat, boxlon, username, password);
                    HttpResponseMessage response = client.GetAsync(queryParamater).Result;
                    var device = response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode)
                    {
                        Console.WriteLine("Response Opaque Box Login: {0}", response.ReasonPhrase);

                    }
                    else
                    {
                        Console.WriteLine("Response Opaque Box Login: {0}", response.ReasonPhrase);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex.Message, "An error occurred within reading the OpaqueBoxLogin() "+ex.InnerException.InnerException.Message);
                Console.WriteLine("An error occurred within reading the OpaqueBoxLogin(): " + ex.InnerException.InnerException.Message);
            }
        }




















        public static string ReadMCCMNC(string mccmnc)
        {
            try
            {

                using (var reader = new StreamReader("configfiles/mcc-mnc-operator-list.csv"))
                using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
                {
                    var good = new List<OperatorMncMcc>();
                    var bad = new List<string>();
                    var isRecordBad = false;
                    csv.Configuration.BadDataFound = context =>
                    {
                        isRecordBad = true;
                        bad.Add(context.RawRecord);
                    };
                    csv.Read();
                    csv.ReadHeader();


                    while (csv.Read())
                    {
                        var record = csv.GetRecord<OperatorMncMcc>();

                        if (!isRecordBad)
                        {
                            good.Add(record);
                        }

                        isRecordBad = false;

                    }

                    if (good.Contains(new OperatorMncMcc { MCCMNC = mccmnc }))
                    {
                        return mccmnc;
                    }
                    if (bad.Contains(mccmnc))
                    {
                        return mccmnc;
                    }
                    else
                    {
                        return mccmnc = mccmnc.Substring(0, 5);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            return mccmnc;
        }








        /// <summary>
        /// Upload the specified imsi, imei, cellid, mcc, mnc, lac, mno, lon, lat, boxmacaddr, country, brand,
        /// imsioperator, datetime, signalStrength and msisdn.
        /// </summary>
        /// <returns>The upload.</returns>
        /// <param name="imsi">Imsi.</param>
        /// <param name="imei">Imei.</param>
        /// <param name="cellid">Cellid.</param>
        /// <param name="mcc">Mcc.</param>
        /// <param name="mnc">Mnc.</param>
        /// <param name="lac">Lac.</param>
        /// <param name="mno">Mno.</param>
        /// <param name="lon">Lon.</param>
        /// <param name="lat">Lat.</param>
        /// <param name="boxmacaddr">Boxmacaddr.</param>
        /// <param name="country">Country.</param>
        /// <param name="brand">Brand.</param>
        /// <param name="imsioperator">Imsioperator.</param>
        /// <param name="datetime">Datetime.</param>
        /// <param name="signalStrength">Signal strength.</param>
        /// <param name="msisdn">Msisdn.</param>
        public static async Task UploadLocation(string imsi, string imei, string cellid, string mcc, string mnc, string lac, string mno, string boxlon, string boxlat, string moblon, string moblat, string boxmacaddr, string country, string brand, string imsioperator, string datetime, string signalStrength, string msisdn)
        {
            try
            {
                ////https://www.myiapps.net/opaque/radiorec/radiorec.php?imsi=%20655020408441923&tmsi=%27%27&msisdn=%27%27&mno=001&imei=abc123qwe&cellid=123456&mcc=010&mnc=123&lac=82345&signalStrength=00.123&lng=-28.7230&lat=-23.9982&locatorBoxID=client-1
                Console.WriteLine("Connecting to Opaque...");
                Console.WriteLine("\n");
                var client = new HttpClient();
                //www.myiapps.net/opaque/testData/index.php?
                Console.WriteLine("myappsurl: {0}", myiappsurl.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("imsi: {0}", imsi);
                Console.WriteLine("cellid: {0}", cellid);
                Console.WriteLine("mcc: {0}", mcc);
                Console.WriteLine("mnc: {0}", mnc);
                Console.WriteLine("lac: {0}", lac);
                Console.WriteLine("Box unique id: "+boxuniqueid);
                Console.WriteLine("box longitude: {0}", boxlon);
                Console.WriteLine("box latitude: {0}", boxlat);
                Console.WriteLine("datetime: {0}", datetime);
                Console.WriteLine("Signal Strength: {0}", signalStrength);
                Console.WriteLine("mobile latitude: {0}", moblat);
                Console.WriteLine("mobile longitude: {0}", moblon);
                Console.WriteLine("msisdn: {0}", msisdn);
                Console.WriteLine("imei: {0}", imei);
                Console.WriteLine("country: {0}", country);
                Console.WriteLine("brand: {0}", brand);
                Console.WriteLine("imsioperator: {0}", imsioperator);

                // phone location: -25.72514524,28.20545401 1st attempt
                //box macaddress is actually the box unique id {boxmacaddr}
                //https://www.myiapps.net/opaque/radiorec/radiorec.php?imsi=%{0}&tmsi=%27%27&msisdn=%27%27&mno={1}&imei=abc123qwe&cellid=123456&mcc={2}&mnc={3}&lac={4}&signalStrength=00.123&lng={5}&lat={6}&locatorBoxID={7}
                string testurl = String.Format(myiappsurl, imsi, imsioperator, mcc, mnc, lac,moblon, moblat, boxuniqueid );
                var result = await client.GetAsync(testurl);

                Console.WriteLine("Sent the data over to Opaque for processing....");
                Console.WriteLine("\n");
                Console.WriteLine("Opaque says the following: " + result.StatusCode);
                Console.WriteLine("\n");

                if(result.StatusCode == HttpStatusCode.OK)
                {
                    try
                    {
                        using (var updateObserver = new ObserveContext())
                        {
                            var stdObservertbl = updateObserver.Observations.Where((c => c.Imsi == imsi && c.Sent == 0)).ToList();
                            if (stdObservertbl != null)
                            {
                                Console.WriteLine("UPDATING Observations table sent 0 to sent 1");
                                stdObservertbl.ForEach(b => b.Sent = 1);
                                updateObserver.SaveChanges();
                            }
                        }


                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("An error occurred while updating Observations Table: " + ex.Message);
                        logger.Error(ex.Message, "An error occurred while updating Observations Table ");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex.Message, "An error occurred within reading the UploadLocation() " + ex.InnerException.InnerException.Message);
                Console.WriteLine("An error occurred within reading the UploadLocation(): " + ex.InnerException.InnerException.Message);

            }


        }






        public static async Task BoxLoginPepla(string uniqueboxid)
        {
            //1.get id of the device in database
            try
            {

                using (var client = new HttpClient())
                {

                    var queryParamater = string.Format("http://opaque.pepla.co.za/api/DeviceTbs/Login?opaqueid={0}", uniqueboxid);
                    HttpResponseMessage response = client.GetAsync(queryParamater).Result;
                    var device = response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode)
                    {
                        var Device = JsonConvert.DeserializeObject<Device>(device.Result);
                        Console.WriteLine("id {0}", Device.id);
                        Console.WriteLine("opaqueid {0}", Device.opaqueid);
                        Console.WriteLine("macaddress {0}", Device.macaddress);
                        Console.WriteLine("publicip {0}", Device.publicip);
                        Console.WriteLine("privateip {0}", Device.privateip);
                        Console.WriteLine("dateentrycreated {0}", Device.dateentrycreated);
                        Console.WriteLine("location {0}", Device.location);
                        Device.token = token;


                        bool reg = RegisterBoxAsOnline(Device);
                        bool getcomm = GetCommandToExecuteID(Device);
                    }
                    if (response.ReasonPhrase == "Not Found")
                    {
                        Device newdevice = new Device();
                        newdevice.id = 0;
                        newdevice.opaqueid = uniqueboxid;
                        newdevice.macaddress = macaddr;
                        newdevice.publicip = boxpublicip;
                        newdevice.privateip = boxprivateip;
                        newdevice.dateentrycreated = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                        newdevice.location = "lat:" + boxlatitude + ";lon:" + boxlongitude;
                        newdevice.token = "string";
                        newdevice.encryptedId = "string";

                        string jsonObject = JsonConvert.SerializeObject(newdevice);
                        var content = new StringContent(jsonObject.ToString(), Encoding.UTF8, "application/json");
                        string accessTokens = token; //"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ1bmlxdWVfbmFtZSI6InVuaXF1ZW9wYXF1ZWlkIiwibmJmIjoxNTg4MjUwMzM0LCJleHAiOjE2MDQwNjE1MzQsImlhdCI6MTU4ODI1MDMzNH0.ancYWsVPWEmfda1fPAjjMQs30uczbTNnTR8c7aEe_TQ";
                        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + accessTokens);
                        var result = client.PostAsync("http://opaque.pepla.co.za/api/DeviceTbs", content).Result;

                       
                    }
                }

            }
            catch (Exception ex)
            {
                logger.Error(ex.Message, "An error occurred within reading the BoxLoginPepla() ");
                Console.WriteLine("An error occurred within reading the BoxLoginPepla(): " + ex.Message);
            }
        }



        public static bool GetCommandToExecuteID(Device device)
        {
            try
            {

                using (var client = new HttpClient())
                {
                   
                    var accessToken = device.token;
                    client.DefaultRequestHeaders.Add("Authorization", "Bearer " + accessToken);
                    var queryParamater = string.Format("http://opaque.pepla.co.za/api/CommandExecutedTbs/isexecuted?deviceid={0}&isexecuted={1}",device.id,0);
                    HttpResponseMessage response = client.GetAsync(queryParamater).Result;
                    var commandtoexecute = response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode)
                    {
                        var executecommandinfo = JsonConvert.DeserializeObject<CommandExecuted>(commandtoexecute.Result);
                       

                        Console.WriteLine("id {0}", executecommandinfo.id);
                        Console.WriteLine("commandid {0}", executecommandinfo.commandid);
                        Console.WriteLine("deviceid {0}", executecommandinfo.deviceid);
                        Console.WriteLine("dateentrycreated {0}", executecommandinfo.dateentrycreated);
                        Console.WriteLine("isexecuted {0}", executecommandinfo.isexecuted);
                        //Console.WriteLine("token {0}", executecommandinfo.token);
                        //Console.WriteLine("encryption {0}", executecommandinfo.encryptedId);


                        string comm = Command(executecommandinfo.commandid, device);
                        //bool ischanged = ChangeCommandToExecute(executecommandinfo.id, executecommandinfo.commandid, executecommandinfo.deviceid, executecommandinfo.dateentrycreated);
                        ProcessCommand(comm);
                    }
                }

            }
            catch (Exception ex)
            {
                logger.Error(ex.Message, "An error occurred within reading the GetCommandToExecuteID() ");
                Console.WriteLine("An error occurred within reading the GetCommandToExecuteID(): " + ex.Message);
            }
            return true;
        }

        public static void ProcessCommand(string comm)
        {
            if(comm=="")
            {
                //do nothing.
            }
            else
            {
                string commandname = comm.Split(':')[0];
                string command = comm.Split(':')[1];

                if(commandname == "restart")
                {
                    RunScriptsFromAPI(command);
                }
                else if(commandname == "special")
                {
                    RunScriptsFromAPI(command);
                }
                else if(commandname == "reset")
                {
                    //truncate all tables in database
                    listOfDbs();
                }
                else if(commandname == "restore")
                {
                    //mysql < all_databases.sql
                    RunScriptsFromAPI(command);
                }
                else if(commandname == "wipe")
                {
                    RunScriptsFromAPI(command);
                    //wipe entire disk
                }
                else if(commandname == "changelocation")
                {
                    string changelat = command.Split(',')[0];
                    string changelon = command.Split(',')[1];
                }
            }
        }


        public static void ChangeLocation(string chlat, string chlon)
        {
            string line = null;
            List<string> lines = new List<string>();
            StreamReader reader = new StreamReader("/home/marietjie/configfiles/appsettings.txt");
            // read all the lines in the file and store them in the List
            while ((line = reader.ReadLine()) != null)
            {
                lines.Add(line);
            }
            reader.Close();
            // change the second line long:28.200579
            lines[1] = "long:" + chlon;
            lines[2] = "lat:" + chlat;
            StreamWriter writer = new StreamWriter("/home/marietjie/configfiles/appsettings.txt", false);
            Console.WriteLine("Writing lines back to the file");
            // write the lines back to the file, overwriting the original one
            for (int i = 0; i < lines.Count; i++)
            {
                if (i < lines.Count - 1)
                {
                    writer.WriteLine(lines[i]);
                    Console.WriteLine(lines[i]);
                }
                else
                {
                    writer.WriteLine(lines[i]);
                    Console.WriteLine(lines[i]);
                }
            }
            writer.Close();
        }



        /// <summary>
        /// Changes toexecute from 0 to 1
        /// </summary>
        /// <returns><c>true</c>, if command to execute was changed, <c>false</c> otherwise.</returns>
        /// <param name="device">Device.</param>
        public static bool ChangeCommandToExecute(int executecommandid, int commandid, int deviceid, string datetime)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    CommandChangeExecuted executedcommand = new CommandChangeExecuted();

                    executedcommand.id = executecommandid;
                    executedcommand.fkCommandTb = commandid;
                    executedcommand.fkDeviceTb = deviceid;
                    executedcommand.dateentrycreated = datetime;
                    executedcommand.isexecuted = 1;
                    executedcommand.token = "string";
                    executedcommand.encryptedId = "string";
                    string jsonObject = JsonConvert.SerializeObject(executedcommand);
                    var content = new StringContent(jsonObject.ToString(), Encoding.UTF8, "application/json");
                    string accessTokens = token; //"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ1bmlxdWVfbmFtZSI6InVuaXF1ZW9wYXF1ZWlkIiwibmJmIjoxNTg4MjUwMzM0LCJleHAiOjE2MDQwNjE1MzQsImlhdCI6MTU4ODI1MDMzNH0.ancYWsVPWEmfda1fPAjjMQs30uczbTNnTR8c7aEe_TQ";
                    client.DefaultRequestHeaders.Add("Authorization", "Bearer " + accessTokens);
                    string url = string.Format("http://opaque.pepla.co.za/api/CommandExecutedTbs/PutCommandExecuted?id={0}", executecommandid);
                    var result = client.PutAsync(url,content).Result;
                    return true;
                }

            }
            catch (Exception ex)
            {
                logger.Error(ex.Message, "An error occurred within reading the CheckForCommands() ");
                Console.WriteLine("An error occurred within reading the CheckForCommands(): " + ex.Message);
            }

            return true;
        }


        public static string Command(int commandid, Device device)
        {
            string commandname = "";
            string command = "";
            try
            {

                using (var client = new HttpClient())
                {

                    var accessToken = device.token;
                    client.DefaultRequestHeaders.Add("Authorization", "Bearer " + accessToken);
                    //http://opaque.pepla.co.za/api/CommandTbs/commandid?commandid=1
                    var queryParamater = string.Format("http://opaque.pepla.co.za/api/CommandTbs/commandid?commandid={0}",commandid);
                    HttpResponseMessage response = client.GetAsync(queryParamater).Result;
                    var commandtoexecute = response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode)
                    {
                        var commandinfo = JsonConvert.DeserializeObject<Command>(commandtoexecute.Result);


                        Console.WriteLine("id {0}", commandinfo.id);
                        Console.WriteLine("commandid {0}", commandinfo.commandname);
                        Console.WriteLine("deviceid {0}", commandinfo.command);
                        Console.WriteLine("dateentrycreated {0}",commandinfo.dateentrycreated);;
                        //Console.WriteLine("token {0}", commandinfo.token);
                        //Console.WriteLine("encryption {0}", commandinfo.encryptedId);


                        commandname = commandinfo.commandname;
                        command = commandinfo.command;

                    }
                }

            }
            catch (Exception ex)
            {
                logger.Error(ex.Message, "An error occurred within reading the CheckForCommands() ");
                Console.WriteLine("An error occurred within reading the CheckForCommands(): " + ex.Message);
            }
            return commandname+":"+command;
        }





        public static void RunScriptsFromAPI(string command)
        {


            Process proc = new System.Diagnostics.Process();
            proc.StartInfo.FileName = "/bin/bash";
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardInput = true;
            proc.StartInfo.RedirectStandardOutput = true;

            proc.Start();

            proc.StandardInput.WriteLine(command);

            proc.StandardInput.WriteLine("exit");
            string line = "";

            while (!proc.StandardOutput.EndOfStream)
            {
                line = proc.StandardOutput.ReadLine();
                Console.WriteLine(line);
            }

            proc.WaitForExit();
        }



        public static void listOfDbs()
        {
            List<string> dbcommands = new List<string>();
            dbcommands.Add("mysqldump -u root --all-databases > all_databases.sql");
            dbcommands.Add("mysql -Nse 'show tables' oai_db | while read table; do mysql -e \"truncate table $table\" oai_db; done");

            foreach (var item in dbcommands)
            {
                RunScriptsFromAPI(item);
            }

        }










        public static bool RegisterBoxAsOnline(Device device)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    LiveDevice liveDevice = new LiveDevice();
                    liveDevice.id = 0;
                    liveDevice.isonline = 1;
                    liveDevice.fkDeviceTb = device.id;
                    liveDevice.dateentrycreated = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");//"2020-05-12T18:16:41.856Z";
                    liveDevice.token = "string";
                    liveDevice.encryptedId = "string";
                    string jsonObject = JsonConvert.SerializeObject(liveDevice);
                    var content = new StringContent(jsonObject.ToString(), Encoding.UTF8, "application/json");
                    string accessTokens = token; //"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ1bmlxdWVfbmFtZSI6InVuaXF1ZW9wYXF1ZWlkIiwibmJmIjoxNTg4MjUwMzM0LCJleHAiOjE2MDQwNjE1MzQsImlhdCI6MTU4ODI1MDMzNH0.ancYWsVPWEmfda1fPAjjMQs30uczbTNnTR8c7aEe_TQ";
                    client.DefaultRequestHeaders.Add("Authorization", "Bearer " + accessTokens);
                    var result = client.PostAsync("http://opaque.pepla.co.za/api/LivedeviceTbs/PostLivedeviceTb", content).Result;
                    return true;
                }

            }
            catch (Exception ex)
            {
                logger.Error(ex.Message, "An error occurred within reading the RegisterBoxAsOnline() ");
                Console.WriteLine("An error occurred within reading the RegisterBoxAsOnline(): " + ex.Message);
            }

            return true;
        }

       





















































     


       

       
       

       






















        /// <summary>
        /// Currents the domain process exit.
        /// </summary>
        /// <param name="sender">Sender.</param>
        /// <param name="e">E.</param>
        private static void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            tokenSource.Cancel();
        }

        /// <summary>
        /// Reads the URL COnfig file.
        /// </summary>
        public static void ReadUrlCOnfigFile()
        {
            try
            {
                string file = "/home/marietjie/configfiles/urls.conf";
                if (File.Exists(file))
                {
                    string[] lines = File.ReadAllLines(file);

                    Console.WriteLine("Reading urls.conf file:");
                    Console.WriteLine("=============================");
                    Console.WriteLine("\n");


                    username = lines[0].ToString().Split(':')[0].ToString();
                    password = lines[0].ToString().Split(':')[1].ToString();


                    enbconf = lines[1];
                    Console.WriteLine(enbconf);
                    Console.WriteLine("\n");

                    myiappsurl = lines[2];
                    Console.WriteLine(myiappsurl);
                    Console.WriteLine("\n");

                    ftpurl = lines[3];
                    Console.WriteLine(ftpurl);
                    Console.WriteLine("\n");

                    openaircnurl = lines[4];
                    Console.WriteLine(openaircnurl);
                    Console.WriteLine("\n");

                    openairinterfaceurl = lines[5];
                    Console.WriteLine(openairinterfaceurl);
                    Console.WriteLine("\n");

                    observerurl = lines[6];
                    Console.WriteLine(observerurl);
                    Console.WriteLine("\n");

                    mmeconf = lines[7];
                    Console.WriteLine(mmeconf);
                    Console.WriteLine("\n");

                    radiovalidationurl = lines[8];
                    Console.WriteLine(radiovalidationurl);
                    Console.WriteLine("\n");


                    Console.WriteLine("\n");
                    Console.WriteLine("=============================");
                    Console.WriteLine("\n");

                }
                else
                {
                    Console.WriteLine("url.conf file is missing");
                    logger.Error("url.conf file is missing ");
                }


            }
            catch (Exception ex)
            {
                logger.Error(ex.Message, "An error occurred within reading the ReadURLConfig() ");
                Console.WriteLine("An error occurred within reading the ReadURLConfig(): " + ex.Message);
            }
        }

        /// <summary>
        /// Reads the appsettings.txt file
        /// </summary>
        /// <returns>The app settings file.</returns>
        public static int ReadAppSettingsFile()
        {
            int convertSecToNanoSec = 0;
            try
            {
                string file = "/home/marietjie/configfiles/appsettings.txt";
                if (File.Exists(file))
                {
                    string[] lines = File.ReadAllLines(file);

                    Console.WriteLine("Reading appsettings.txt file:");
                    Console.WriteLine("=============================");
                    Console.WriteLine("\n");


                    convertSecToNanoSec = Int32.Parse(lines[0].ToString().Split(':')[1].ToString()) * 1000;
                    Console.WriteLine(convertSecToNanoSec.ToString());
                    Console.WriteLine("\n");

                    boxlongitude = lines[1].ToString().Split(':')[1].ToString();
                    Console.WriteLine(boxlongitude);
                    Console.WriteLine("\n");
                    boxlatitude = lines[2].ToString().Split(':')[1].ToString();
                    Console.WriteLine(boxlatitude);
                    Console.WriteLine("\n");
                    country = lines[3].ToString().Split(':')[1].ToString();
                    Console.WriteLine(country);
                    Console.WriteLine("\n");
                    boxuniqueid = lines[4].ToString().Split(':')[1].ToString();
                    Console.WriteLine(boxuniqueid);
                    Console.WriteLine("\n");
                    installer_username = lines[5].ToString().Split(':')[1].ToString();
                    Console.WriteLine(installer_username);
                    Console.WriteLine("\n");
                    installer_password = lines[6].ToString().Split(':')[1].ToString();
                    Console.WriteLine(installer_password);
                    Console.WriteLine("\n");
                    token = lines[7].ToString().Split(':')[1].ToString();
                    Console.WriteLine(token);

                    Console.WriteLine("\n");
                    Console.WriteLine("=============================");
                    Console.WriteLine("\n");

                }
                else
                {
                    Console.WriteLine("appsettings.txt file is missing");
                    logger.Error("appsettings.txt file is missing ");
                }


            }
            catch (Exception ex)
            {
                logger.Error(ex.Message, "An error occurred within reading the ReadAppSettingsFile() ");
                Console.WriteLine("An error occurred within reading the ReadAppSettingsFile(): " + ex.Message);
            }
            return convertSecToNanoSec;
        }

        /// <summary>
        /// if GetMacAddress() do not work, then try this method
        /// </summary>
        /// <returns>The first mac address.</returns>
        public static string GetFirstMacAddress()
        {
            string macAddresses = string.Empty;


            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus == OperationalStatus.Up)
                {

                    macAddresses += nic.GetPhysicalAddress().ToString();
                    break;
                }
            }

            if (macAddresses == "")
            {
                Console.WriteLine("Second attempt to find mac address failed, please investigate physical network layer why no mac addresses cannot be found.");
                logger.Error("Second attempt to find mac address failed, please investigate physical network layer why no mac addresses cannot be found.");
            }

            return macAddresses;
        }

        public static string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            throw new Exception("No network adapters with an IPv4 address in the system!");
        }

        /// <summary>
        ///  Trying to find a mac address
        /// </summary>
        /// <returns>The mac address.</returns>
        public static string GetMacAddress()
        {
            const int MIN_MAC_ADDR_LENGTH = 12;
            string macAddress = string.Empty;


            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                Console.WriteLine("Found MAC Address: " + nic.GetPhysicalAddress() +
                    " Type: " + nic.NetworkInterfaceType);


                string tempMac = nic.GetPhysicalAddress().ToString();
                if (!string.IsNullOrEmpty(tempMac) &&
                    tempMac.Length >= MIN_MAC_ADDR_LENGTH)
                {

                    macAddress = tempMac;
                }
            }
            Console.WriteLine("======================================================");
            Console.WriteLine("\n");

            return macAddress;
        }

        /// <summary>
        /// Programs the NLogger.
        /// </summary>
        public static void ProgramNLogger()
        {
            var config = new NLog.Config.LoggingConfiguration();

            // Targets where to log to: File and Console
            var logfile = new NLog.Targets.FileTarget("logfile") { FileName = "Logs.txt" };
            var logconsole = new NLog.Targets.ConsoleTarget("logconsole");

            // Rules for mapping loggers to targets            
            config.AddRule(LogLevel.Trace, LogLevel.Error, logconsole);
            config.AddRule(LogLevel.Trace, LogLevel.Error, logfile);

            // Apply config           
            NLog.LogManager.Configuration = config;

        }

        /// <summary>
        /// Calibrates the blade rf.
        /// </summary>
        public static void CalibrateBladeRF(string dlearfcn, string ulearfcn)
        {
            string command = "bladeRF-cli -i";
            // Process.Start("bladeRF - cli - i");
            Process proc = new System.Diagnostics.Process();
            proc.StartInfo.FileName = "/bin/bash";
            proc.StartInfo.Arguments = "-c \" " + command + " \"";
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardInput = true;
            proc.StartInfo.RedirectStandardOutput = true;

            proc.Start();




            //string calLms = "cal lms"; //no longer needed
            string calDcrxtx = "cal dc rxtx";
            string stExit = "exit";
            string line = "";

            string setdownlink = string.Format("set frequency tx {0}",dlearfcn);
            string setuplink = string.Format("set frequency rx {0}", ulearfcn);

            Console.WriteLine("Calibrating the blade to fit the network");
            // RX1 Frequency: 1767600000 Hz(Range: [237500000, 3800000000])
            // TX1 Frequency: 1862600000 Hz(Range: [237500000, 3800000000])
            proc.StandardInput.WriteLine(setdownlink);
            proc.StandardInput.WriteLine(setuplink);
            proc.StandardInput.WriteLine("set bandwidth 5000000");
            proc.StandardInput.WriteLine("set samplerate 30720000");

            /*switch (filename)
            {
                case "vodamme.conf":

                    Console.WriteLine("Calibrating Vodacom Frequency");
                    // RX1 Frequency: 1767600000 Hz(Range: [237500000, 3800000000])
                    // TX1 Frequency: 1862600000 Hz(Range: [237500000, 3800000000])
                    proc.StandardInput.WriteLine("set frequency tx 1862600000");
                    proc.StandardInput.WriteLine("set frequency rx 1767600000");
                    proc.StandardInput.WriteLine("set bandwidth 5000000");


                    break;
                case "mtnmme.conf":

                    //2533800000 ul
                    //2653800000 dl
                    Console.WriteLine("Calibrating MTN Frequency");
                    proc.StandardInput.WriteLine("set frequency tx 1822700000");
                    proc.StandardInput.WriteLine("set frequency rx 1727700000");
                    proc.StandardInput.WriteLine("set bandwidth 5000000");

                    break;
                case "cellcmme.conf":
                    Console.WriteLine("Calibrating CellC Frequency");
                    proc.StandardInput.WriteLine("set frequency tx 1847500000");
                    proc.StandardInput.WriteLine("set frequency rx 1752500000");
                    proc.StandardInput.WriteLine("set bandwidth 5000000");
                    break;
                case "telkommme.conf":
                    Console.WriteLine("Calibrating Telkom Frequency");
                    proc.StandardInput.WriteLine("set frequency tx 1836100000");
                    proc.StandardInput.WriteLine("set frequency rx 1741100000");
                    proc.StandardInput.WriteLine("set bandwidth 5000000");
                    break;
                default:
                    break;
            }*/

            //proc.StandardInput.WriteLine(calLms);
            //proc.StandardInput.WriteLine(calLms);
            //proc.StandardInput.WriteLine(calLms);
            proc.StandardInput.WriteLine(calDcrxtx);
            proc.StandardInput.WriteLine(calDcrxtx);
            proc.StandardInput.WriteLine(calDcrxtx);
            proc.StandardInput.WriteLine(stExit);


            while (!proc.StandardOutput.EndOfStream)
            {
                line = proc.StandardOutput.ReadLine();
                Console.WriteLine(line);
            }

            proc.WaitForExit();

        }

        /// <summary>
        /// Patchs the free diameter.
        /// </summary>
        public static void PatchFreeDiameter()
        {


            Process proc = new System.Diagnostics.Process();
            proc.StartInfo.FileName = "/bin/bash";
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardInput = true;
            proc.StartInfo.RedirectStandardOutput = true;

            proc.Start();

            proc.StandardInput.WriteLine("cd");
            proc.StandardInput.WriteLine("cd freediameter/build");
            proc.StandardInput.WriteLine("cmake -DCMAKE_INSTALL_PREFIX:PATH=/usr/local ../");
            proc.StandardInput.WriteLine("make -j4");
            proc.StandardInput.WriteLine("sudo make install");
            proc.StandardInput.WriteLine("exit");
            string line = "";

            while (!proc.StandardOutput.EndOfStream)
            {
                line = proc.StandardOutput.ReadLine();
                Console.WriteLine(line);
            }

            proc.WaitForExit();

        }

        /// <summary>
        /// Starts the hss.
        /// </summary>
        public static void StartHSS()
        {

            string command = string.Format("{0}", "./configfiles/start_hss.sh");
            Process proc = new System.Diagnostics.Process();
            proc.StartInfo.FileName = "gnome-terminal";
            proc.StartInfo.UseShellExecute = true;
            proc.StartInfo.RedirectStandardInput = false;
            proc.StartInfo.RedirectStandardOutput = false;
            proc.StartInfo.Arguments = " -e  \" " + command + " \"";

            proc.Start();

        }

        /// <summary>
        /// Starts the mme.
        /// </summary>
        public static void StartMME()
        {

            string command = string.Format("{0}", "./configfiles/start_mme.sh");
            Process proc = new System.Diagnostics.Process();
            proc.StartInfo.FileName = "gnome-terminal";
            proc.StartInfo.UseShellExecute = true;
            proc.StartInfo.RedirectStandardInput = false;
            proc.StartInfo.RedirectStandardOutput = false;
            proc.StartInfo.Arguments = " -e  \" " + command + " \""; 

            proc.Start();

        }

        /// <summary>
        /// Starts the lte.
        /// </summary>
        public static void StartLTE()
        {

            string command = string.Format("{0}", "./configfiles/start_lte.sh");
            Process proc = new System.Diagnostics.Process();
            proc.StartInfo.FileName = "gnome-terminal";
            proc.StartInfo.UseShellExecute = true;
            proc.StartInfo.RedirectStandardInput = false;
            proc.StartInfo.RedirectStandardOutput = false;
            proc.StartInfo.Arguments = " -e  \" " + command + " \"";

            proc.Start();


        }

        /// <summary>
        /// Kills the HSS script.
        /// This script only gets killed when
        /// the program gets restarted by the user
        /// This is to ensure that the hss is not interupted
        /// when writing data to the database 
        /// </summary>
        public static void KillHSSScript()
        {
            Console.WriteLine("Going to kill any HSS programs if any exist:");
            Process proc = new System.Diagnostics.Process();
            proc.StartInfo.FileName = "/bin/bash";
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardInput = true;
            proc.StartInfo.RedirectStandardOutput = true;

            proc.Start();

            proc.StandardInput.WriteLine("killall - 9 oai_hss");
            proc.StandardInput.WriteLine("killall -9 run_hss");

            proc.StandardInput.WriteLine("exit");
            string line = "";

            while (!proc.StandardOutput.EndOfStream)
            {
                line = proc.StandardOutput.ReadLine();
                Console.WriteLine(line);
            }

            proc.WaitForExit();
        }

        /// <summary>
        /// Kills the scripts lte, mme.
        /// </summary>
        public static void KillLTEMMEScripts()
        {

            Console.WriteLine("Going to kill any MME and LTE programs if any exist:");
            Process proc = new System.Diagnostics.Process();
            proc.StartInfo.FileName = "/bin/bash";
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardInput = true;
            proc.StartInfo.RedirectStandardOutput = true;

            proc.Start();

            proc.StandardInput.WriteLine("killall -9 lte-softmodem");

            proc.StandardInput.WriteLine("killall - 9 oai_mme");
            proc.StandardInput.WriteLine("killall -9 run_mme");


            proc.StandardInput.WriteLine("exit");
            string line = "";

            while (!proc.StandardOutput.EndOfStream)
            {
                line = proc.StandardOutput.ReadLine();
                Console.WriteLine(line);
            }

            proc.WaitForExit();
        }

        /// <summary>
        /// Rotate between network files
        /// </summary>
        /// <param name="networks">Networks.</param>
        public static void NetworkRotation(List<string> networks, List<string> mmes, List<string> mccs, List<string>towerdetails,List<string>earfcndetails, string countrymccfile)
        {


            string networkfilename = "";
            string mmefilename = "";
            string mccfilename = "";
            string earfcnfilename = "";

            networks.Sort();
            mmes.Sort();
            mccs.Sort();
            earfcnlist.Sort();

            while (true)
            {

                for (int i = 0; i < networks.Count; i++)
                {



                    try
                     {
                         KillLTEMMEScripts();

                         Thread.Sleep(200);
                         networkfilename = networks[i];
                         mmefilename = mmes[i];
                         mccfilename = mccs[i];
                         earfcnfilename = earfcndetails[i];
                         Console.WriteLine(networks[i]);
                         Console.WriteLine(mmes[i]);
                         Console.WriteLine(mccs[i]);
                         Console.WriteLine(earfcndetails[i]);
                    
                         try
                         {
                             string line = null;
                             List<string> lines = new List<string>();
                             StreamReader reader = new StreamReader(earfcnfilename);
                             // read all the lines in the file and store them in the List
                             while ((line = reader.ReadLine()) != null)
                             {
                                 lines.Add(line);
                             }
                             reader.Close();
                             string earfcndl = lines[0];
                             string earfcnul = lines[1];
                             CopyEnbFiles(networkfilename);
                             Thread.Sleep(200);
                             CopyMMEFiles(mmefilename, earfcndl, earfcnul);
                             Thread.Sleep(200);
                         }
                         catch (Exception ex)
                         {
                             Console.WriteLine("Inside Rotation Network reading earfcn info");
                             Console.WriteLine(ex.Message);
                         }


                         ReadNetworkSetupFiles(mccfilename);
                         Console.WriteLine(networkfilename);
                         //Thread.Sleep(60000);//60sec
                     }
                     catch (Exception ex)
                     {
                         Console.WriteLine("At Rotation Network");
                         Console.WriteLine(ex.Message);
                     }
                    Console.WriteLine("Network Rotation Thread is sleeping");
                    Thread.Sleep(seconds);
                }
            }
        }

        /// <summary>
        /// Copies the enb files.
        /// </summary>
        /// <param name="filename">Filename.</param>
        public static void CopyEnbFiles(string filename)
        {
            Process proc = new System.Diagnostics.Process();
            proc.StartInfo.FileName = "/bin/bash";
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardInput = true;
            proc.StartInfo.RedirectStandardOutput = true;

            proc.Start();

            string command = string.Format("sudo cp {0} {1}", filename,enbconf);

            proc.StandardInput.WriteLine(command);


            proc.StandardInput.WriteLine("exit");
            string line = "";

            while (!proc.StandardOutput.EndOfStream)
            {
                line = proc.StandardOutput.ReadLine();
                Console.WriteLine(line);
            }

            proc.WaitForExit();
        }

        /// <summary>
        /// Copies the MME config files.
        /// </summary>
        /// <param name="filename">Filename.</param>
        public static void CopyMMEFiles(string filename, string dlearfcn, string ulearfcn)
        {
            Process proc = new System.Diagnostics.Process();
            proc.StartInfo.FileName = "/bin/bash";
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardInput = true;
            proc.StartInfo.RedirectStandardOutput = true;

            proc.Start();

            string command = string.Format("sudo cp {0} {1}", filename, mmeconf);

            proc.StandardInput.WriteLine(command);


            proc.StandardInput.WriteLine("exit");
            string line = "";

            while (!proc.StandardOutput.EndOfStream)
            {
                line = proc.StandardOutput.ReadLine();
                Console.WriteLine(line);
            }

            proc.WaitForExit();



            CalibrateBladeRF(dlearfcn,ulearfcn);
            Thread.Sleep(100);


            PatchFreeDiameter();
            Thread.Sleep(100);

            StartMME();
            Thread.Sleep(500);
            StartLTE();

        }

        /// <summary>
        /// Reads the network setup.
        /// </summary>
        /// <param name="file">File.</param>
        public static void ReadNetworkSetupFiles(string file)
        {
            try
            {

                if (File.Exists(file))
                {
                    string[] lines = File.ReadAllLines(file);

                    Console.WriteLine("Reading {0} file:", file);
                    Console.WriteLine("=============================");
                    Console.WriteLine("\n");

                    cellid = lines[0].ToString().Split(':')[1].ToString();
                    Console.WriteLine(cellid);
                    Console.WriteLine("\n");
                    mcc = int.Parse(lines[1].ToString().Split(':')[1].ToString());
                    Console.WriteLine(mcc);
                    Console.WriteLine("\n");
                    mnc = int.Parse(lines[2].ToString().Split(':')[1].ToString());
                    Console.WriteLine(mnc);
                    Console.WriteLine("\n");
                    lac = lines[3].ToString().Split(':')[1].ToString();
                    Console.WriteLine(lac);
                    Console.WriteLine("\n");
                    brand = lines[4].ToString().Split(':')[1].ToString();
                    Console.WriteLine(brand);
                    Console.WriteLine("\n");
                    imsioperator = lines[5].ToString().Split(':')[1].ToString();
                    Console.WriteLine(imsioperator);
                    Console.WriteLine("\n");
                    Console.WriteLine("=============================");
                    Console.WriteLine("\n");

                }
                else
                {
                    Console.WriteLine("{0} file is missing", file);
                    logger.Error("{0} file is missing ", file);
                }


            }
            catch (Exception ex)
            {
                logger.Error(ex.Message, "An error occurred within reading the ReadNetworkSetupFiles() ");
                Console.WriteLine("An error occurred within reading the ReadNetworkSetupFiles(): " + ex.Message);
            }
        }



    }


}