using System;
using CsvHelper.Configuration.Attributes;

namespace Observer
{
    public class OperatorMncMcc
    {
        [Index(0)]
        public string MCCMNC { get; set; }
    }

}
