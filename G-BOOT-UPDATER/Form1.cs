using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Net.NetworkInformation;
using System.Threading;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using System.Net.Sockets;
using System.Net;
using Sunny.UI;

namespace G_BOOT_UPDATER
{
    public partial class Form1 : Form
    {
        private KeyboardHook _keyboardHook;

        private const string SERVER_IP = "192.168.1.122";
        private const int SERVER_PORT = 5000;
        private const string ACK_MSG = "ACK";
        private const string CRC_OK_MSG = "CRC_OK";
        private const int BUFFER_SIZE = 256;
        private string firmwareFilePath;
        public Form1()
        {
            InitializeComponent();
            this.StartPosition = FormStartPosition.CenterScreen;

            _keyboardHook = new KeyboardHook();
            _keyboardHook.KeyPressed += KeyboardHook_KeyPressed;
            _keyboardHook.Start();
        }

        private void KeyboardHook_KeyPressed(Keys key)
        {
            if (Control.ModifierKeys.HasFlag(Keys.Control) &&
                Control.ModifierKeys.HasFlag(Keys.Alt) &&
                key == Keys.Space)
            {
                Invoke(new Action(() => uiButton2.PerformClick())); // 触发 button2 点击事件
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            _keyboardHook.Stop();
        }

        private void button1_Click(object sender, EventArgs e)
        {

        }
        private void Form1_Load(object sender, EventArgs e)
        {
            //timer1.Enabled = true;
        }

        private bool IsPortOpen(string host, int port, int timeout)
        {
            try
            {
                using (TcpClient tcpClient = new TcpClient())
                {
                    var result = tcpClient.BeginConnect(host, port, null, null);
                    bool success = result.AsyncWaitHandle.WaitOne(timeout);
                    if (!success)
                    {
                        return false;
                    }
                    tcpClient.EndConnect(result);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private uint CalculateCRC32(string filePath)
        {
            uint crc32 = 0xFFFFFFFF;
            byte[] buffer = new byte[BUFFER_SIZE];

            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                int bytesRead;
                while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (int i = 0; i < bytesRead; i++)
                    {
                        crc32 ^= buffer[i];
                        for (int j = 0; j < 8; j++)
                        {
                            if ((crc32 & 1) == 1)
                            {
                                crc32 = (crc32 >> 1) ^ 0xEDB88320;
                            }
                            else
                            {
                                crc32 >>= 1;
                            }
                        }
                    }
                }
            }

            return crc32 ^ 0xFFFFFFFF;
        }
        private void SendFirmware()
        {
            try
            {
                byte[] firmwareData = File.ReadAllBytes(firmwareFilePath);
                int firmwareSize = firmwareData.Length;
                uint firmwareCRC32 = CalculateCRC32(firmwareFilePath);

                Console.WriteLine(firmwareCRC32);
                //label1.Text = firmwareCRC32.ToString();


                // 连接 TCP 服务器
                using (TcpClient tcpClient = new TcpClient(SERVER_IP, SERVER_PORT))
                using (NetworkStream stream = tcpClient.GetStream())
                {
                    // 发送固件大小（4 字节，网络字节序）
                    byte[] sizeData = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(firmwareSize));
                    stream.Write(sizeData, 0, sizeData.Length);

                    // 发送固件数据
                    int sentBytes = 0;
                    for (int i = 0; i < firmwareSize; i += BUFFER_SIZE)
                    {
                        int chunkSize = Math.Min(BUFFER_SIZE, firmwareSize - i);
                        byte[] chunk = new byte[chunkSize];
                        Array.Copy(firmwareData, i, chunk, 0, chunkSize);
                        stream.Write(chunk, 0, chunk.Length);

                        // 等待服务器 ACK
                        byte[] ack = new byte[ACK_MSG.Length];
                        stream.Read(ack, 0, ack.Length);
                        if (Encoding.ASCII.GetString(ack) != ACK_MSG)
                        {
                            UIMessageBox.Show("传输失败，ACK 校验错误");
                            return;
                        }

                        sentBytes += chunkSize;
                        uiProcessBar1.Value = (int)((double)sentBytes / firmwareSize * 100);
                        //lblProgress.Text = $"传输进度: {sentBytes}/{firmwareSize} 字节 ({(sentBytes / (double)firmwareSize) * 100:F2}%)";
                    }

                    // 发送 CRC32 校验值（4 字节，网络字节序）
                    byte[] crcData = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)firmwareCRC32));
                    stream.Write(crcData, 0, crcData.Length);

                    // 接收服务器端返回的 CRC 校验结果
                    byte[] crcResponse = new byte[CRC_OK_MSG.Length];
                    stream.Read(crcResponse, 0, crcResponse.Length);

                    if (Encoding.ASCII.GetString(crcResponse) == CRC_OK_MSG)
                    {
                        Console.WriteLine("All Right.");
                        //uiLabel3.Visible = true;
                        uiProcessBar1.RectColor = Color.Green;
                        
                    }
                    else
                    {
                        UIMessageBox.Show("CRC32 校验失败，固件可能损坏！");
                    }
                }
            }
            catch (Exception ex)
            {
                UIMessageBox.Show($"❌ 错误：{ex.Message}");
            }
        }

        private void uiButton1_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Title = "选择一个 BIN 文件",
                Filter = "BIN 文件 (*.bin)|*.bin",
                InitialDirectory = string.IsNullOrEmpty(Properties.Settings.Default.LastFilePath)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                    : System.IO.Path.GetDirectoryName(Properties.Settings.Default.LastFilePath),
                Multiselect = false
            };
            
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                string fullPath = openFileDialog.FileName;
                FileInfo fileInfo = new FileInfo(fullPath);

                //uiLabel3.Visible = false;
                firmwareFilePath = fullPath;
                uiButton2.Enabled = true;  // 禁用按钮，防止多次点击
                uiProcessBar1.Value = 0;     // 重置进度条
                uiProcessBar1.RectColor = Color.FromArgb(80, 160, 255);

                // 记住路径
                Properties.Settings.Default.LastFilePath = fullPath;
                Properties.Settings.Default.Save();

                // 获取路径部分
                string[] pathParts = fullPath.Split(Path.DirectorySeparatorChar);
                int maxLevels = 4;
                string shortPath;

                if (pathParts.Length > maxLevels)
                {
                    // 取倒数 5 级路径
                    string[] lastParts = new string[maxLevels];
                    Array.Copy(pathParts, pathParts.Length - maxLevels, lastParts, 0, maxLevels);
                    shortPath = "..." + Path.DirectorySeparatorChar + string.Join(Path.DirectorySeparatorChar.ToString(), lastParts);
                }
                else
                {
                    shortPath = fullPath;
                }

                label1.Text = $"固件路径: {shortPath}\r\n\n" +
                              $"固件大小: {fileInfo.Length / 1024.0:F2} KB\r\n\n" +
                              $"修改日期: {fileInfo.LastWriteTime}";
            }
        }

        private void uiButton2_Click(object sender, EventArgs e)
        {
            //Console.WriteLine("");
            if (string.IsNullOrEmpty(firmwareFilePath))
            {
                UIMessageBox.Show("请选择固件文件！");
                return;
            }

            uiButton2.Enabled = false;  // 禁用按钮，防止多次点击
            uiProcessBar1.Value = 0;     // 重置进度条

            Thread flashThread = new Thread(SendFirmware);
            flashThread.Start();
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            bool isOpen = IsPortOpen("192.168.1.122", 5000, 1000); // 超时时间 1000ms
            //Console.WriteLine($"端口 {port} 是否可用: {isOpen}");

            //timer1.Enabled = true;
            if (isOpen)
            {
                pictureBox1.Visible = true;
            }
            else
            {
                pictureBox2.Visible = true;
            }
        }

        private void timer2_Tick(object sender, EventArgs e)
        {

        }

        private void uiLabel1_Click(object sender, EventArgs e)
        {

        }
    }
}

public class KeyboardHook
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;

    private LowLevelKeyboardProc _proc;
    private IntPtr _hookID = IntPtr.Zero;
    public event Action<Keys> KeyPressed;

    public KeyboardHook()
    {
        _proc = HookCallback;
    }

    public void Start()
    {
        _hookID = SetHook(_proc);
    }

    public void Stop()
    {
        UnhookWindowsHookEx(_hookID);
    }

    private IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using (Process curProcess = Process.GetCurrentProcess())
        using (ProcessModule curModule = curProcess.MainModule)
        {
            return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
                GetModuleHandle(curModule.ModuleName), 0);
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            KeyPressed?.Invoke((Keys)vkCode);
        }
        return CallNextHookEx(_hookID, nCode, wParam, lParam);
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn,
        IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
}

