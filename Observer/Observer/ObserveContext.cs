using System;
using MySql.Data.Entity;
using System.Data.Common;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;

namespace Observer
{
    [DbConfigurationType(typeof(MySqlEFConfiguration))]
    public class ObserveContext : DbContext
    {
        //Entity in Database
        public DbSet<Observation> Observations{ get; set; }
        public DbSet<Measurementreport> Measurementreports { get; set; }
        public DbSet<LocationReport> LocationReports{ get; set; }
        public DbSet<NetworkDetail> NetworkDetails { get; set; }
        public DbSet<TowerDetail> TowerDetails { get; set; }

        public DbSet<Rsrpmeasurement> Rsrpmeasurements { get; set; }

        public DbSet<Operator> Operators { get; set; }








        public ObserveContext() : base("ObserveContext")
        {

        }

        public ObserveContext(DbConnection existingConnection, bool contextOwnsConnection) : base(existingConnection, contextOwnsConnection)
        {

        }
    }
}

