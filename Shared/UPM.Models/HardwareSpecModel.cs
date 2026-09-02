using System;

namespace UPM.Models {
    public class HardwareSpecModel {
        public string CpuName { get; set; } = "Unknown CPU";
        public int CpuCores { get; set; }
        public int CpuThreads { get; set; }
        public string RamTotal { get; set; } = "0 GB";
        public string GpuName { get; set; } = "Unknown GPU";
        public string Mainboard { get; set; } = "Unknown Mainboard";
    }
}