using System;
namespace Observer
{
    public class TowerInfo
    {
       
            private string MCC { get; set; }
            private string MNC { get; set; }
            private string TAC { get; set; }
            private string PCI { get; set; }
            private float LAT { get; set; }
            private float LON { get; set; }
            private float Radius { get; set; }
            private int Aged { get; set; } 

            public TowerInfo ( string mcc, string mnc, string tac, string pci, float lat, float lon, float radius, int aged)
            {
                this.MCC = mcc;
                this.MNC = mnc;
                this.TAC = tac;
                this.PCI = pci;
                this.LAT = lat;
                this.LON = lon;
                this.Radius = radius;
                this.Aged = aged;
            }

    }
}
