using AMS.Profile;
using RemoteErrorDisplay.Source;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using Timer = System.Timers.Timer;

namespace RemoteErrorDisplay
{
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {

        struct PartInfo
        {
            public string Name;
            public int PrefixCount;
        }



        private bool UseZebraPrinter { get; set; }

        #region private constants
        private const double DisplayHeight = 855;//782;
        private const double DisplayWidth = 1280;
        #endregion
        
        
        
        #region Private Fields
        private static Timer _aTimer;
        private readonly int _DelayStart;
        private readonly string _FileName;
        private readonly string _SourceFolder;

        private int _CurrentPartIndex = -1;
        private int _ItemsToRead;
        private PartInfo[] _PartsInfo;

        private List<string>[] _Prefixes;
        private FileSystemWatcher _Watcher;

        #endregion Private Fields

        #region Private Zebra Fields
        private ZebraPrint _ZebraPrinter = new ZebraPrint();
        private IPAddress _PrinterIpAddress;
        private string _StrPrinterIp;
        private int _PrefixStartPos;
        private int _DataStartPos;
        private int _CharacterWidth;
        private int _CharacterHeight;
        #endregion





        #region Public Constructors
        public MainWindow()
        {
            InitializeComponent();
            Xml settings = null;
            //figure out secondary screen and position to display Error screen
            var screens = Screen.AllScreens;

            if (screens.Length == 1)
            {
                MessageBox.Show("Only one monitor found \n Application for two monitor system only", "Monitor Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(-1);
            }

            var firstSecondary = screens.FirstOrDefault(s => s.Primary == false);
            if (firstSecondary != null)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                WindowState = WindowState.Minimized;
                var offsetX = (firstSecondary.Bounds.Width - DisplayWidth) / 2;
                var offsetY = (firstSecondary.Bounds.Height - DisplayHeight) / 2;

                Left = firstSecondary.Bounds.X + offsetX;
                Top = offsetY;
            }

            var processModule = Process.GetCurrentProcess().MainModule;
            if (processModule != null)
            {
                var appPath = processModule.FileName;
                appPath =
                    appPath.Substring(0, appPath.LastIndexOf("\\", appPath.Length - 1, StringComparison.Ordinal)) +
                    "\\";

                //Check that Config.xml exists
                if (!File.Exists(appPath + "Config.xml"))
                {
                    MessageBox.Show("Configuration file not found \n" + appPath + "Config.xml", "Configuration Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    LogFile.WriteString(1, @"Configuration file not found");
                    Environment.Exit(-1);
                }

                //Read parameters from Config.xml
                settings = new Xml(appPath + "Config.xml");
                var loggingLevel = settings.GetValue("AppSettings", "LoggingLevel", 0);
                _SourceFolder = settings.GetValue("AppSettings", "SourceFolder", "");
                _FileName = settings.GetValue("AppSettings", "FileName", "");
                _DelayStart = settings.GetValue("AppSettings", "DelayStart", 0);
                UseZebraPrinter = settings.GetValue("AppSettings", "UseZebraPrinter", false);
                //Check for errors read from config.xml
                if (loggingLevel == 0 || _SourceFolder == "" || _FileName == "")
                {
                    MessageBox.Show("Error in configuration file \n" + appPath + "Config.xml", "Configuration Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    LogFile.WriteString(1, "Error in configuration file");
                    Environment.Exit(-1);
                }


                if (UseZebraPrinter)
                {
                    _StrPrinterIp = settings.GetValue("ZebraPrinter", "PrinterIp", "");
                    _PrefixStartPos = settings.GetValue("ZebraPrinter", "PrefixStartPos", 0);
                    _DataStartPos = settings.GetValue("ZebraPrinter", "DataStartPos", 0);
                    _CharacterWidth = settings.GetValue("ZebraPrinter", "CharacterWidth", 0);
                    _CharacterHeight = settings.GetValue("ZebraPrinter", "CharacterHeight", 0);

                    //Parse string Ip to NetIP
                    if (IPAddress.TryParse(_StrPrinterIp, out _PrinterIpAddress))
                    {
                        if (!_ZebraPrinter.PingIp(_PrinterIpAddress))
                        {
                            LogFile.WriteString(1, "Error Pinging address:" + _PrinterIpAddress);
                            MessageBox.Show("Error Pinging address:" + _PrinterIpAddress, "Network Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                            Environment.Exit(-1);
                        }
                    }
                    else
                    {
                        LogFile.WriteString(1, @"Enter a valid IP Address in configuration file");
                        MessageBox.Show(@"Enter a valid IP Address in configuration file", "Configuration Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        Environment.Exit(-1);
                    }

                }

                LogFile.LoggingLevel = loggingLevel;
                //if source directory does not exist create it
                if (!Directory.Exists(_SourceFolder))
                    try
                    {
                        Directory.CreateDirectory(_SourceFolder);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error creating source folder \n" + ex.Message, "Configuration Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        LogFile.WriteString(1, "Error creating source folder service stopped \n " + ex.Message);
                        Environment.Exit(-1);
                    }
            }

            //If there is already a data file in the directory exists delete it.
            if (File.Exists(_SourceFolder + "\\" + _FileName))
                File.Delete(_SourceFolder + "\\" + _FileName);

            ReadDataConfig(settings);

            _aTimer = new Timer(_DelayStart);
            _aTimer.Elapsed += OnTimerElapsed;
            _aTimer.Enabled = true;
        }
        #endregion Public Constructors

        #region Private Methods
        private void MainWindow_OnInitialized(object sender, EventArgs e)
        {
            LogFile.WriteString(1, "Application Started");
        }

        private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
        {
            Width = DisplayWidth;
            Height = DisplayHeight;
            WindowState = WindowState.Normal;
            Topmost = true;
        }

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            try
            {
                LogFile.WriteString(3, "Created: " + e.Name);
                //Test if this is the file we are looking for
                if (e.Name.ToUpper() == _FileName.ToUpper()) ReadFile(e.FullPath);
            }
            catch (Exception exception)
            {
                LogFile.WriteString(1, "OnCreated::Error reading file " + exception.Message);
            }
        }

        private void OnTimerElapsed(object sender, ElapsedEventArgs e)
        {
            _aTimer.Enabled = false;
            //Configure new instance of File Watcher
            _Watcher = new FileSystemWatcher
            {
                Path = _SourceFolder,
                NotifyFilter = NotifyFilters.LastAccess
                               | NotifyFilters.LastWrite
                               | NotifyFilters.FileName
                               | NotifyFilters.DirectoryName,
                // Only watch text files.
                Filter = "*.txt"
            };

            // Add event handlers.
            _Watcher.Created += OnCreated;
            //_Watcher.Created += new FileSystemEventHandler(OnCreated);
            // watcher.Changed += OnChanged;
            // watcher.Deleted += new FileSystemEventHandler(OnDeleted);
            // watcher.Renamed += OnRenamed;

            //Enable file watch
            _Watcher.EnableRaisingEvents = true;
        }


        private void ReadDataConfig(Xml settings)
        {
            //Read how many part to configure

            int numParts;
            int numItems;

            numParts = settings.GetValue("Parts", "NumberOfParts", 0);

            _PartsInfo = new PartInfo[numParts];
            for (int i = 0; i < numParts; i++)
            {
                _PartsInfo[i].Name = settings.GetValue("Parts", "Part" + (i + 1).ToString(), "");
            }

            _Prefixes = new List<string>[numParts];

            for (int i = 0; i < numParts; i++)
            {
                _Prefixes[i] = new List<string>();
                _PartsInfo[i].PrefixCount = settings.GetValue(_PartsInfo[i].Name, "NumberItems", 0);
                for (int j = 0; j < _PartsInfo[i].PrefixCount - 2; j++)
                {
                    _Prefixes[i].Add(settings.GetValue(_PartsInfo[i].Name, "PrefixItem" + j.ToString(), ""));
                }
            }
        }

        private void ReadFile(string fileName)
        {
            // flag to set if number of parts read matches one stored
            bool matchFound = false;

            //create list to hold file contents as we are not sure how many items are in data file
            List<string> fileContents;

            //Clear any messages on screen
            ClearErrorMessage();

            //Read all the file into an array removing any blank lines in or at end of file
            try
            {
                Thread.Sleep(500);
                fileContents = File.ReadAllLines(fileName).Where(q => !string.IsNullOrWhiteSpace(q)).ToList();

                for (int i = 0; i < _PartsInfo.Length; i++)
                {
                    if (fileContents.Count == _PartsInfo[i].PrefixCount)
                    {
                        matchFound = true;
                        _CurrentPartIndex = i;
                        break;
                    }
                }





                if (!matchFound)
                {
                    LogFile.WriteString(1, "Error reading file wrong number of rows:" + fileContents.Count);
                    //MessageBox.Show("Wrong number of rows in file " + fileContents.Count, "IO Error",
                    //    MessageBoxButton.OK, MessageBoxImage.Error);
                    DisplayErrorMessage("Wrong number of rows in file " + fileContents.Count);
                    
                    File.Delete(fileName);
                    return;
                }
            }
            catch (Exception e)
            {
                //if error reading file log then delete file
                LogFile.WriteString(1, "Error reading file " + e.Message + fileName);
                Thread.Sleep(500);
                File.Delete(fileName);
                return;
            }

            //Delete the file after reading
            try
            {
                File.Delete(fileName);
            }
            catch (Exception e)
            {
                //if error reading file log then delete file
                LogFile.WriteString(1, "Error Deleting file " + e.Message + fileName);
                File.Delete(fileName);
                return;
            }

            var strDate = fileContents[1];
            var strTime = fileContents[0];
            var strCount = fileContents[2];

            var gageMateItems = new List<GageMateItem>();

            for (var i = 3; i < fileContents.Count; i++)
            {
                double dblTemp;
                if (fileContents[i] != "0")
                {
                                        if (double.TryParse(fileContents[i], out  dblTemp))
                                            gageMateItems.Add(new GageMateItem(_Prefixes[_CurrentPartIndex][i-2], dblTemp));
                }
            }

            Application.Current.Dispatcher.Invoke(delegate
            {
                UpdateDisplay(strTime, strDate, strCount, gageMateItems);
            });
            if (UseZebraPrinter && gageMateItems.Count > 0)
                SendToPrinter(strTime, strDate, strCount, gageMateItems);
        }


        //private int _CharacterWidth;
        //private int _CharacterHeight;
        //private int _TearOffAdvance;

        private void SendToPrinter(string time, string date, string count, List<GageMateItem> gageMateItems)
        {
            string zplString = "^XA";
            //zplString += "^LL" + (10 * 200).ToString();
            zplString += "^LL" + ((gageMateItems.Count + 10)*37).ToString();
            zplString += "^FO200,140^ADN," + _CharacterHeight + "," + _CharacterWidth + "^FD" + "Date: " + date + "^FS";
            zplString += "^FO200,175^ADN," + _CharacterHeight + "," + _CharacterWidth + "^FD" + "Time: " + time + "^FS";
            zplString += "^FO200,210^ADN," + _CharacterHeight + "," + _CharacterWidth + "^FD" + "Count: " + count + "^FS";

            int yPos = 280;
            foreach (GageMateItem gageMateItem in gageMateItems)
            {
                yPos += 35;
                zplString += "^FO" + _PrefixStartPos.ToString() + "," + yPos.ToString() + "^ADN," + _CharacterHeight + "," + _CharacterWidth + "^FD" + gageMateItem.Parameter + "^FS";
                zplString += "^FO" + _DataStartPos.ToString() + "," + yPos.ToString() + "^ADN," + _CharacterHeight + "," + _CharacterWidth + "^FD" + gageMateItem.Measurement + "^FS";
            }
            zplString += "^LL" + (200).ToString();
            zplString += "^XZ";

            if (!_ZebraPrinter.PrintTcp(_PrinterIpAddress, zplString))
            {
                Application.Current.Dispatcher.Invoke(delegate
                {
                    TextBlockMessage.Background = Brushes.Aqua;
                    TextBlockMessage.Text = @"Error sending label to printer";
                });

                //var msgReturn = MessageBox.Show("Error sending label to printer \n Check cable connection", "IO Error", MessageBoxButton.OK,
                //      MessageBoxImage.Error);
            }

        }

        private void ClearErrorMessage()
        {
            Application.Current.Dispatcher.Invoke(delegate
            {
                TextBlockMessage.Background = Brushes.Transparent;
                TextBlockMessage.Text = "";
            });
        }


        private void DisplayErrorMessage(string message)
        {
            Application.Current.Dispatcher.Invoke(delegate
            {
                DataGrid1.Visibility = Visibility.Hidden;
                DataGrid2.Visibility = Visibility.Hidden;
                DataGrid3.Visibility = Visibility.Hidden;
                TextBlockTime.Text = "";
                TextBlockDate.Text = "";
                TextBlockCount.Text = "";
                TextPartName.Text = "";
                BorderDisplay.Background = (Brush)new BrushConverter().ConvertFrom("#FFF73B4C");
                TextBlockMessage.Background = Brushes.Aqua;
                TextBlockMessage.Text = message;
            });

        }



        private void UpdateDisplay(string time, string date, string count, List<GageMateItem> gageMateItems)
        {
            TextBlockTime.Text = time;
            TextBlockDate.Text = date;
            TextBlockCount.Text = count;
            TextPartName.Text = _PartsInfo[_CurrentPartIndex].Name;

            List<GageMateItem>[] arrayGageMateItems;

            DataGrid1.Visibility = Visibility.Hidden;
            DataGrid2.Visibility = Visibility.Hidden;
            DataGrid3.Visibility = Visibility.Hidden;

            if (gageMateItems.Count == 0)
            {
                //DataGridGageMate.ItemsSource = null; //gageMateItems;
                BorderDisplay.Background = (Brush)new BrushConverter().ConvertFrom("#FF5BFB62");
            }
            else
            {
                BorderDisplay.Background = (Brush)new BrushConverter().ConvertFrom("#FFF73B4C");
                if (gageMateItems.Count > 40)
                {
                    arrayGageMateItems = new List<GageMateItem>[3];

                    arrayGageMateItems[0] = new List<GageMateItem>();
                    arrayGageMateItems[1] = new List<GageMateItem>();
                    arrayGageMateItems[2] = new List<GageMateItem>();

                    arrayGageMateItems[0] = gageMateItems.GetRange(0, 20).ToList();
                    arrayGageMateItems[1] = gageMateItems.GetRange(20, 20).ToList();
                    arrayGageMateItems[2] = gageMateItems.GetRange(40, gageMateItems.Count - 40).ToList();

                    Grid.SetColumn(DataGrid1, 1);
                    Grid.SetColumnSpan(DataGrid1, 2);

                    DataGrid2.BorderThickness = new Thickness(4, 4, 0, 4);
                    DataGrid1.ItemsSource = arrayGageMateItems[0];
                    DataGrid1.Items.Refresh();
                    DataGrid1.Visibility = Visibility.Visible;

                    Grid.SetColumn(DataGrid2, 3);
                    Grid.SetColumnSpan(DataGrid2, 2);

                    DataGrid2.BorderThickness = new Thickness(0, 4, 0, 4);
                    DataGrid2.ItemsSource = arrayGageMateItems[1];
                    DataGrid2.Items.Refresh();
                    DataGrid2.Visibility = Visibility.Visible;


                    Grid.SetColumn(DataGrid3, 5);
                    Grid.SetColumnSpan(DataGrid3, 2);

                    DataGrid3.ItemsSource = arrayGageMateItems[2];
                    DataGrid3.Items.Refresh();
                    DataGrid3.Visibility = Visibility.Visible;
                }
                if (gageMateItems.Count <= 40 && gageMateItems.Count > 20)
                {
                    arrayGageMateItems = new List<GageMateItem>[3];

                    arrayGageMateItems[0] = new List<GageMateItem>();
                    arrayGageMateItems[1] = new List<GageMateItem>();

                    arrayGageMateItems[0] = gageMateItems.GetRange(0, 20).ToList();
                    arrayGageMateItems[1] = gageMateItems.GetRange(20, gageMateItems.Count - 20).ToList();

                    Grid.SetColumn(DataGrid1, 2);
                    Grid.SetColumnSpan(DataGrid1, 2);

                    DataGrid1.BorderThickness = new Thickness(4, 4, 2, 4);
                    DataGrid1.ItemsSource = arrayGageMateItems[0];
                    DataGrid1.Items.Refresh();
                    DataGrid1.Visibility = Visibility.Visible;

                    //Grid3
                    Grid.SetColumn(DataGrid3, 4);
                    Grid.SetColumnSpan(DataGrid3, 2);

                    DataGrid1.BorderThickness = new Thickness(2, 4, 4, 4);
                    DataGrid3.ItemsSource = arrayGageMateItems[1];
                    DataGrid3.Items.Refresh();
                    DataGrid3.Visibility = Visibility.Visible;
                }

                if (gageMateItems.Count <= 20)
                {
                    Grid.SetColumn(DataGrid2, 3);
                    Grid.SetColumnSpan(DataGrid2, 2);

                    DataGrid2.BorderThickness = new Thickness(4, 4, 4, 4);
                    DataGrid2.ItemsSource = gageMateItems;
                    DataGrid2.Items.Refresh();
                    DataGrid2.Visibility = Visibility.Visible;
                }
            }
        }

        private void MainWindow_OnClosed(object sender, EventArgs e)
        {
            LogFile.WriteString(1, "Application Ended");
        }
        #endregion Private Methods
    }
}