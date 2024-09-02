using System;
using System.IO.Ports;

public class Brain{

    private const int MAX_PACKET_LENGTH = 32; // 定义最大数据包长度
    private const int EEG_POWER_BANDS = 8;    // 定义EEG功率频带数量

    private SerialPort brainStream;
    private byte[] packetData;
    private bool inPacket;
    private byte latestByte;
    private byte lastByte;
    private int packetIndex;
    private int packetLength;
    private byte checksum;
    private byte checksumAccumulator;
    private int eegPowerLength;
    private bool hasPower;
    private string latestError;
    private string csvBuffer;
    private byte signalQuality;
    private byte attention;
    private byte meditation;
    private bool freshPacket;
    private uint[] eegPower;

    public Brain(SerialPort _brainStream){
        brainStream = _brainStream;
        packetData = new byte[MAX_PACKET_LENGTH];
        eegPower = new uint[EEG_POWER_BANDS];
        init();
    }

    private void init(){
        freshPacket = false;
        inPacket = false;
        packetIndex = 0;
        packetLength = 0;
        eegPowerLength = 0;
        hasPower = false;
        checksum = 0;
        checksumAccumulator = 0;

        signalQuality = 200;
        attention = 0;
        meditation = 0;
        ClearEegPower();
    }

    public bool update(){
        // get the number of bytes that can be read in the input buffer
        if (brainStream.BytesToRead > 0) {

            // read one byte of data, returns an integer representing the bytes read.
            latestByte = (byte)brainStream.ReadByte(); 

            if (inPacket) { // Build a packet if we know we're and not just listening for sync bytes.
                if (packetIndex == 0) {
                    packetLength = latestByte;
                    if (packetLength > MAX_PACKET_LENGTH) {
                        latestError = $"ERROR: Packet too long {packetLength}";
                        inPacket = false;
                    }
                } else if (packetIndex <= packetLength) {
                    packetData[packetIndex - 1] = latestByte;
                    checksumAccumulator += latestByte;
                } else if (packetIndex > packetLength) {
                    // We're at the end of the data payload.

                    // Check the checksum.
                    checksum = latestByte;
                    checksumAccumulator = (byte)(255 - checksumAccumulator);

                    // Do they match?
                    if (checksum == checksumAccumulator) {
                        if (ParsePacket()) {
                            freshPacket = true;
                        }
                        else {
                            latestError = "ERROR: Could not parse";
                        }
                    }
                    else {
                        latestError = "ERROR: Checksum";
                    }

                    inPacket = false;
                }

                packetIndex++;
            }

            // Look for the start of the packet 0xAA
            if (latestByte == 170 && lastByte == 170 && !inPacket) {
                inPacket = true;
                packetIndex = 0;
                checksumAccumulator = 0;
            }

            lastByte = latestByte;
        }

        if (freshPacket) {
            freshPacket = false;
            return true;
        }
        else {
            return false;
        }
    }

    private void clearPacket() {
        // private byte[] packetData;
        Array.Clear(packetData, 0, packetData.Length);
    }

    private void clearEegPower() {
        // Zero the power bands.
        Array.Clear(eegPower, 0, eegPower.Length);
    }

    private bool parsePacket() {
        // Loop through the packet, extracting data.
        // Based on mindset_communications_protocol.pdf from the Neurosky Mindset SDK.
        // Returns true if passing succeeds
        hasPower = false;
        bool parseSuccess = true;
        int rawValue = 0;
        ClearEegPower();

        for (int i = 0; i < packetLength; i++) {
            switch (packetData[i]) {
                case 0x02:
                    signalQuality = packetData[++i];
                    break;
                case 0x04:
                    attention = packetData[++i];
                    break;
                case 0x05:
                    meditation = packetData[++i];
                    break;
                case 0x83:  // EEG功率数据标识符
                    i++;
                    for (int j = 0; j < EEG_POWER_BANDS; j++) {
                        eegPower[j] = (uint)((packetData[++i] << 16) | (packetData[++i] << 8) | packetData[++i]);
                    }
                    hasPower = true;
                    break;
                case 0x80:  // 原始脑波数据标识符（未使用）
                    i++;  // 跳过长度字节
                    rawValue = (packetData[++i] << 8) | packetData[++i];
                    break;
                default: // 如果遇到未识别的数据标识符
                    parseSuccess = false;
                    break;
            }
        }
        return parseSuccess;
    }

    public void PrintCSV(){
        // 打印CSV格式的脑电波数据
        string csvOutput = $"{signalQuality},{attention},{meditation}";

        if (hasPower) {
            for (int i = 0; i < eegPower.Length; i++) {
                csvOutput += $",{eegPower[i]}";
            }
        }

        Console.WriteLine(csvOutput);  // 打印输出到控制台或者其他串行流
    }

    public string readErrors(){ 
        return latestError;
    }    
    public string readCSV() {
        if (hasPower) {
            csvBuffer = $"{signalQuality},{attention},{meditation},{string.Join(",", eegPower)}";
        }
        else {
            csvBuffer = $"{signalQuality},{attention},{meditation}";
        }
        return csvBuffer;
    }

    private void printDebug() {
        Console.WriteLine("--- Start Packet ---");
        Console.WriteLine($"Signal Quality: {signalQuality}");
        Console.WriteLine($"Attention: {attention}");
        Console.WriteLine($"Meditation: {meditation}");

        if (hasPower) {
            Console.WriteLine("EEG POWER:");
            Console.WriteLine($"Delta: {eegPower[0]}");
            Console.WriteLine($"Theta: {eegPower[1]}");
            Console.WriteLine($"Low Alpha: {eegPower[2]}");
            Console.WriteLine($"High Alpha: {eegPower[3]}");
            Console.WriteLine($"Low Beta: {eegPower[4]}");
            Console.WriteLine($"High Beta: {eegPower[5]}");
            Console.WriteLine($"Low Gamma: {eegPower[6]}");
            Console.WriteLine($"Mid Gamma: {eegPower[7]}");
        }

        Console.WriteLine($"Checksum Calculated: {checksumAccumulator}");
        Console.WriteLine($"Checksum Expected: {checksum}");
        Console.WriteLine("--- End Packet ---");
    }

    
    public byte readSignalQuality() => signalQuality;
    public byte readAttention() => attention;
    public byte readMeditation() => meditation;
    public uint[] readPowerArray() => eegPower;

    public uint readDelta() => eegPower[0];

    public uint readTheta() => eegPower[1];

    public uint readLowAlpha() => eegPower[2];

    public uint readHighAlpha() => eegPower[3];

    public uint readLowBeta() => eegPower[4];

    public uint readHighBeta() => eegPower[5];

    public uint readLowGamma() => eegPower[6];

    public uint readMidGamma() => eegPower[7];
}

// 主函数类，用于测试 Brain 类
class Program
{
    static void Main()
    {
        // 模拟串行端口通信（例如，COM3）
        string portName = "COM3"; // 替换为你的实际串口端口
        int baudRate = 9600;
        SerialPort brainStream = new SerialPort(portName, baudRate);

        // 打开串口
        brainStream.Open();

        // 创建 Brain 对象
        Brain brain = new Brain(brainStream);

        // 循环读取数据并测试解析功能
        while (true)
        {
            try
            {
                // 更新数据
                if (brain.Update())
                {
                    // 打印 CSV 格式的数据
                    brain.PrintCSV();
                }

                // 为避免死循环
                Thread.Sleep(100); 
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误: {ex.Message}");
                break; // 如果发生错误，退出循环
            }
        }

        // 关闭串口
        brainStream.Close();
    }
}

