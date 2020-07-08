using System;
namespace Observer
{
    public class Device
    {
        public int id { get; set; }
        public string opaqueid { get; set; }
        public string macaddress { get; set; }
        public string publicip { get; set; }
        public string privateip { get; set; }
        public string dateentrycreated { get; set; }
        public string location { get; set; }
        public string token { get; set; }
        public string encryptedId { get; set; }

    }

    public class LiveDevice
    {
        public int id { get; set; }
        public int isonline { get; set; }
        public int fkDeviceTb { get; set; }
        public string dateentrycreated { get; set; }
        public string token { get; set; }
        public string encryptedId { get; set; }

    }

    public class CommandExecuted
    {
        //"{\"id\":1,\"commandid\":1,\"deviceid\":1,\"dateentrycreated\":\"2020-04-29T21:33:00\",\"isexecuted\":0}"
        public int id { get; set; }
        public int commandid { get; set; }
        public int deviceid { get; set; }
        public string dateentrycreated { get; set; }
        public int isexecuted { get; set; }
        //public string token { get; set; }
        //public string encryptedId { get; set; }

    }

    public class CommandChangeExecuted
    {
        //"{\"id\":1,\"commandid\":1,\"deviceid\":1,\"dateentrycreated\":\"2020-04-29T21:33:00\",\"isexecuted\":0}"
        public int id { get; set; }
        public int fkCommandTb { get; set; }
        public int fkDeviceTb { get; set; }
        public string dateentrycreated { get; set; }
        public int isexecuted { get; set; }
        public string token { get; set; }
        public string encryptedId { get; set; }

    }

    public class Command
    {
        //"{\"id\":1,\"commandname\":\"restart\",\"command\":\"sudo shutdown -r\",\"dateentrycreated\":\"2020-04-29T21:32:00\"}"
        public int id { get; set; }
        public string commandname { get; set; }
        public string command { get; set; }
        public string dateentrycreated { get; set; }
        //public string token { get; set; }
        //public string encryptedId { get; set; }

    }
}
