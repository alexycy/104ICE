using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace WpfApp1
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// 所有命令，包含发送和接收
        /// </summary>
        private ObservableCollection<CommandInfo> _allCmdInfo = new ObservableCollection<CommandInfo>();
        private Socket _clientSocket;
        /// <summary>
        /// 发送序列号
        /// </summary>
        private int _sendSequenceNumber = 0;

        /// <summary>
        /// 接收序列号
        /// </summary>
        private int _receiveSequenceNumber = 0;

        public MainWindow()
        {
            InitializeComponent();
            dg.ItemsSource = _allCmdInfo;

        }

        //连接服务器
        private void ConnectServer(object sender, RoutedEventArgs e)
        {
            IPEndPoint ipe = new IPEndPoint(IPAddress.Parse(ServerIpTbx.Text), int.Parse(ServerPortTbx.Text));
            _clientSocket = new Socket(ipe.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            _clientSocket.Connect(ipe);
            _allCmdInfo.Add(new CommandInfo() { Desc = "连接服务器成功", Foreground = Brushes.LightSeaGreen });


            //接收线程
            Task.Factory.StartNew(() =>
            {
                while (true)
                {
                    if (_clientSocket.Connected)
                    {
                        var buffer = new byte[255];
                        var len = _clientSocket.Receive(buffer);
                        if (len > 0)
                        {
                            if ((buffer[2]&0b1)==0)
                            {
                                //i帧的时候，接收序列号+1
                                _receiveSequenceNumber++;
                            }
                            //s帧的时候，也给服务发个s帧
                            if ((buffer[2] & 0b1) == 1&& ((buffer[2]>>1) & 0b1) == 0)
                            {
                                var cmd = Protocal104Helper.SuperviseCmd(_receiveSequenceNumber);
                                SendByte(cmd);
                            }


                            Dispatcher.Invoke(() =>
                            {
                                _allCmdInfo.Add(new CommandInfo()
                                {
                                    RawData = buffer.Take(len).ToArray(),
                                    Foreground = Brushes.Red
                                });
                            });
                        }
                    }
                }
            });
        }


        void SendByte(byte[] bts)
        {
            _clientSocket.Send(bts, bts.Length, SocketFlags.None);
          
            Dispatcher.Invoke(() =>
            {
                _allCmdInfo.Add(new CommandInfo()
                {
                    RawData = bts,
                    Foreground = Brushes.Green,
                });
            });
        }

        /// <summary>
        /// 启动帧
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SendUStart(object sender, RoutedEventArgs e)
        {
            var bts = Protocal104Helper.UStartCmd();
            SendByte(bts);
        }

          
        /// <summary>
        /// 时间同步
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SendTimeSync(object sender, RoutedEventArgs e)
        {
            var bts = Protocal104Helper.TimeCmd(_sendSequenceNumber,_receiveSequenceNumber);
            _sendSequenceNumber++;

            SendByte(bts);
            
        }



        private void LoopSendClockSync(object sender, RoutedEventArgs e)
        {
            //循环发送时间同步
            Task.Factory.StartNew(() =>
            { 
                while (true)
                {
                    SendTimeSync(null,null);
                    Thread.Sleep(1000);
                } 
            }); 

        }
    }



    public class CommandInfo : INotifyPropertyChanged
    {
        private string _cmd;
        private string _chineseInfo;

        public CommandInfo()
        {
            Time = DateTime.Now;
        }

        /// <summary>
        /// 短描述
        /// </summary>
        public string Desc
        {
            get => _desc;
            set
            {
                _desc = value;
                OnPropertyChanged();
            }
        }

        public DateTime Time { get; set; }

        public string Cmd
        {
            get => _cmd;
            set
            {
                _cmd = value;
                OnPropertyChanged();
            }
        }


        public Brush Foreground { get; set; }

        public string ChineseInfo
        {
            get => _chineseInfo; set
            {
                _chineseInfo = value;
                OnPropertyChanged();
            }
        }

        private byte[] _rawData;
        private string _desc;

        public byte[] RawData
        {
            get { return _rawData; }
            set
            {
                _rawData = value;

                ParseCmd();
            }
        }



        void ParseCmd()
        {
            if (_rawData == null)
            {
                return;
            }

            if (_rawData.Length < 6)
            {
                return;
            }

            Cmd = string.Join(" ", _rawData.Take(_rawData.Length).Select(x => x.ToString("X2")));
            //u帧，i帧，s帧
            string shortInfo = "";
            string longInfo = "";

            longInfo += "Apdu长度:" + _rawData[1] + "\r\n";

            var b1 = _rawData[2] & 0b1;
            var b2 = (_rawData[2] & 0b10) >> 1;
            if (b1 == 1 && b2 == 1)
            {
                //(未编号控制)
                shortInfo += "U帧,";
                var b3 = (_rawData[2] & 0b100) >> 2;
                var b4 = (_rawData[2] & 0b1000) >> 3;
                var b5 = (_rawData[2] & 0b10000) >> 4;
                var b6 = (_rawData[2] & 0b100000) >> 5;
                var b7 = (_rawData[2] & 0b1000000) >> 6;
                var b8 = (_rawData[2] & 0b10000000) >> 7;
                if (b3 == 1)
                {
                    shortInfo += "启动生效,";
                }
                else if (b4 == 1)
                {
                    shortInfo += "启动确认,";
                }
                else if (b5 == 1)
                {
                    shortInfo += "终止生效,";
                }
                else if (b6 == 1)
                {
                    shortInfo += "终止确认,";
                }
                else if (b7 == 1)
                {
                    shortInfo += "测试生效,";
                }
                else if (b8 == 1)
                {
                    shortInfo += "测试确认";
                }
            }
            else if (b1 == 1 && b2 == 0)
            {
                //(编号监视)
                shortInfo += "S帧,";
                 
                //发送序列号
                var sendSer = (_rawData[3] << 8) + (_rawData[2] >> 1);
                longInfo += "发送序列号：" + sendSer + "\r\n";
                //接收序列号
                var recvSer = (_rawData[5] << 8) + (_rawData[4] >> 1);
                longInfo += "接收序列号：" + recvSer + "\r\n";
            }
            else if (b1 == 0)
            {
                //(编号信息传输)
                shortInfo += "I帧,";


                //发送序列号
                var sendSer = (_rawData[3] << 8) + (_rawData[2] >> 1);
                longInfo += "发送序列号：" + sendSer + "\r\n";
                //接收序列号
                var recvSer = (_rawData[5] << 8) + (_rawData[4] >> 1);
                longInfo += "接收序列号：" + recvSer + "\r\n";


                //九字节固定
                //类型标识（TYP）：1字节

                //    可变结构限定词（VSQ）：1字节

                //    b）传送原因（COT）：2字节

                //    c）ASDU公共地址（ADR）：2字节

                //    d）信息对象地址（InfoAdr）：3字节
                if (_rawData.Length >= 15)
                {
                    ParseIFrameData(_rawData.Skip(6).Take(_rawData.Length - 6).ToArray(), out string sInfo, out string lInfo);
                    shortInfo += sInfo;
                    longInfo += lInfo;

                    ParseTypeDetail(_rawData, out string s, out string l);
                    shortInfo += s;
                    longInfo += l;
                }
            }

            Desc = shortInfo;
            ChineseInfo = longInfo;

        }

        private void ParseTypeDetail(byte[] rawData, out string s, out string l)
        {
            var asduType = rawData[6];
            if (asduType == 0x67)
            {
                if (rawData.Length != 6 + 9 + 7)
                {
                    l = "";
                    s = $"时间格式不对,长度{rawData.Length}不等于检验长度{6 + 9 + 7}\r\n";
                    return;
                }
                else
                {
                    var tmByte = rawData.Skip(6 + 9).Take(rawData.Length - 15).ToArray();
                    var tm = Protocal104Helper.Byte2Time(tmByte);
                    s = "";
                    l = "时间:" + tm.ToString("yyyy-MM-dd HH:mm:ss fff") + "\r\n";
                }

            }
            else
            {
                s = asduType.ToString("X2") + " 类型未实现" + "\r\n";
                l = "";
            }
        }

        private void ParseIFrameData(byte[] iFrameAsdu, out string shortInfo, out string longInfo)
        {
            shortInfo = "";
            longInfo = "";
            //类型标识
            var tp = iFrameAsdu[0];
            shortInfo += "类型标识：" + GetType(tp) + "\r\n";
            //可变结构限定词
            var sInfo = iFrameAsdu[1];
            longInfo += (sInfo & 0b10000000) == 1 ? "可变结构：连续（地址，元素，地址，元素）" : "可变结构：不连续（地址，元素，元素）" + "\r\n个数：" + (sInfo & 0b01111111) + "\r\n";
            //传输原因
            longInfo += "传输原因:" + GetROT(iFrameAsdu[2], iFrameAsdu[3]) + "\r\n";
            //asdu公共地址
            var pubAdd = iFrameAsdu[4] + (iFrameAsdu[5] << 8);
            longInfo += "公共地址RTU地址：" + pubAdd + "\r\n";
            //信息对象地址
            var infoAddr = iFrameAsdu[6] + (iFrameAsdu[7] << 8) + (iFrameAsdu[8] << 16);
            longInfo += "信息对象地址：" + infoAddr + "\r\n";
        }

        //传输原因
        private string GetROT(byte b1, byte b2)
        {
            if (b1 == 0x06)
            {
                return "主站-激活";
            }
            else if (b1 == 0x08)
            {
                return "主站-停止激活";
            }

            else if (b1 == 0x03)
            {
                return "从站-突发";
            }
            else if (b1 == 0x05)
            {
                return "从站-被请求";
            }
            else if (b1 == 0x07)
            {
                return "从站-激活确认";
            }
            else if (b1 == 0x09)
            {
                return "从站-停止激活确认";
            }
            else if (b1 == 0x0a)
            {
                return "从站-激活停止";
            }
            else if (b1 == 0x2c)
            {
                return "从站-未知类型标识";
            }
            else if (b1 == 0x2d)
            {
                return "从站-未知传输原因";
            }
            else if (b1 == 0x2e)
            {
                return "从站-未知公共地址";
            }
            else if (b1 == 0x2f)
            {
                return "从站-未知信息对象地址";
            }
            else
            {
                return "未知-" + b1.ToString("X2") + b2.ToString("X2");
            }

        }

        //asdu类型
        string GetType(byte tp)
        {
            if (tp == 01)
            {
                return "遥信-不带时标的单点遥信，每个遥信占1个字节";
            }
            else if (tp == 03)
            {
                return "遥信-不带时标的双点遥信，每个遥信占1个字节";
            }
            else if (tp == 0x14)
            {
                return "遥信-具有状态变位检出的成组单点遥信，每个字节8个遥信";
            }





            else if (tp == 9)
            {
                return "遥测-带品质描述的测量值，每个遥测值占3个字节";
            }
            else if (tp == 0x0a)
            {
                return "遥测-带3个字节时标的且具有品质描述的测量值，每个遥测值占6个字节";
            }
            else if (tp == 0x0b)
            {
                return "遥测-不带时标的标准化值，每个遥测值占3个字节";
            }
            else if (tp == 0x0c)
            {
                return "遥测-带3个时标的标准化值，每个遥测值占6个字节";
            }
            else if (tp == 0x0d)
            {
                return "遥测-带品质描述的浮点值，每个遥测值占5个字节";
            }
            else if (tp == 0x0E)
            {
                return "遥测-带3个字节时标且具有品质描述的浮点值，每个遥测值占8个字节";
            }
            else if (tp == 0x15)
            {
                return "遥测- 不带品质描述的遥测值，每个遥测值占1个字节";
            }





            else if (tp == 0x0F)
            {
                return "遥脉- 不带时标的电能量，每个电能量占5个字节";
            }
            else if (tp == 0x10)
            {
                return "遥脉- 带3个字节短时标的电能量，每个电能量占8个字节";
            }
            else if (tp == 0x25)
            {
                return "遥脉- 带7个字节短时标的电能量，每个电能量占12个字节";
            }


             
            else if (tp == 0x02)
            {
                return "SOE- 带3个字节短时标的单点遥信";
            }

            else if (tp == 0x04)
            {
                return "SOE- 带3个字节短时标的双点遥信";
            }

            else if (tp == 0x1E)
            {
                return "SOE- 带7个字节短时标的单点遥信";
            }
            else if (tp == 0x1F)
            {
                return "SOE- 带7个字节短时标的双点遥信";
            }





            else if (tp == 0x2E)
            {
                return "其他-  双点遥控";
            }
            else if (tp == 0x2F)
            {
                return "其他-   双点遥调";
            }
            else if (tp == 0x64)
            {
                return "其他-   召唤全数据（总召唤）";
            }

            else if (tp == 0x65)
            {
                return "其他-   召唤全电度";
            }
            else if (tp == 0x67)
            {
                return "其他-   时钟同步";
            }

            else
            {
                return "未知-" + tp;
            }
        }



        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
