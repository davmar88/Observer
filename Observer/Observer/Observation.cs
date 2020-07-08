using System;

namespace Observer
{
    public class Observation
    {
        public int Id { get; set; }
        public string Imsi { get; set; }
        public DateTime Created_At { get; set; }
        public int Sent { get; set; }
    }

    public class Measurementreport
    {
        public int Id { get; set; }
        public int Cellid { get; set; }
        public int Rsrp { get; set; }
        public string Imsi { get; set; }
        public DateTime Created_At { get; set; }
        public int Sent { get; set; }
    }

    public class LocationReport
    {
        public int Id { get; set; }
        public string Network_Id { get; set; }
        public string Ue_Id { get; set; }

    }

    public class NetworkDetail
    {
        public int Id { get; set; }
        public int Id_locationreport_fk { get; set; }
        public string Cellid { get; set; }
        public string Rsrp { get; set; }
        public string Rsrq { get; set; }
        public DateTime Created_At { get; set; }
        public int Processed { get; set; }
    }

    public class TowerDetail
    {
        public int Id { get; set; }
        public int Mcc { get; set; }
        public int Mnc { get; set; }
        public int Tac { get; set; }
        public int Pci { get; set; }
        public float Lat { get; set; }
        public float Lon { get; set; }
        public float Range_meter { get; set; }
        public int Aged { get; set; }
        public float Rsrp_per_meter { get; set; }
    }

    public class Rsrpmeasurement
    {
        public int Id{ get; set; }
        public int Id_Observations_fk { get; set; }
        public int Id_AvailNetworks_fk { get; set; }
        public float Total_meter { get; set; }
        public int Processed { get; set; }
    }




    public class TowerReadings
    {
        public int Pci { get; set; }
        public float Lat { get; set; }
        public float Lon { get; set; }
        public float Range_meter { get; set; }

        public TowerReadings(int pci, float lat, float lon, float meter)
        {
            this.Pci = pci;
            this.Lat = lat;
            this.Lon = lon;
            this.Range_meter = meter;
        }
    }

    public class MeasurementreportReadings
    {
        public int Cellid { get; set; }
        public int Rsrp { get; set; }

        public MeasurementreportReadings(int cellid, int rsrp)
        {
            this.Cellid = cellid;
            this.Rsrp = rsrp;
        }
    }

    public class Operator
    {
        public int Id { get; set; }
        public string MCCMNC { get; set; }
        public string MCC { get; set; }
        public string MNC { get; set; }
        public string Country { get; set; }
        public string Network { get; set; }
    }

    public class TowerMeasurementReportCombined
    {
        public int Cellid { get; set; }
        public double Rsrp { get; set; }
        public float Lat { get; set; }
        public float Lon { get; set; }
        public string Imsi { get; set; }
        public string Mcc { get; set; }
        public string Mnc { get; set; }
        public string Mno { get; set; }
        public int Lac { get; set; }


        public TowerMeasurementReportCombined(int cellid,double rsrp, float lat, float lon,string imsi, string mcc, string mnc, string mno, int lac)
        {
            this.Cellid = cellid;
            this.Rsrp = rsrp;
            this.Lat = lat;
            this.Lon = lon;
            this.Imsi = imsi;
            this.Mcc = mcc;
            this.Mnc = mnc;
            this.Mno = mno;
            this.Lac = lac;

        }
    }


}
