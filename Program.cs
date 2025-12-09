using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.ServiceProcess;
using System.IO;
using System.Linq;

namespace BC_Startup
{
    internal static class Program
    {
        static Mutex mutex = new Mutex(true, "{E8F5859B-B9F5-475B-B73A-2567D14A6C16}");

        [System.Runtime.InteropServices.DllImport("kernel32")]
        extern static UInt64 GetTickCount64();

        static readonly string navServiceName = "MicrosoftDynamicsNavServer$LSPOS";
        //static readonly string NavServiceName = "spooler";

        [STAThread]
        static void Main()
        {
            //check if another instance of BC_Startup is already running
           if (!mutex.WaitOne(0, true))
{
    Environment.Exit(0);
}

            //if nav serviec is running then launch straight away
            if (IsServiceRunning(navServiceName))
            {               
                StartAppShell();
                System.Environment.Exit(0);
            }

            //nav service is not running so create the form and wait
            if (mutex.WaitOne(0, true))
            {
                //handler for when form closes
                System.Windows.Forms.Application.ApplicationExit += new EventHandler(OnApplicationExit);
                InitializeStartupForm();
            }
            else
                System.Windows.Forms.Application.Exit();

        }

        static void InitializeStartupForm()
        {
            Form StartupForm = CreateForm("BC Service is Starting, Please Wait ...");
            Task.Run(() => BCStartup(StartupForm));
            StartupForm.ShowDialog();
        }

        static Form CreateForm(string Text)
        {
            Icon bcicon;
            try
            {
                bcicon = new Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BC.ico"));
            }
            catch (FileNotFoundException)
            {
                bcicon = SystemIcons.Information;  
            }
            Form StartupForm = new Form
            { 
                Text = "BC Startup",
                Width = 450,
                Height = 120,
                BackColor = Color.FromArgb(52, 30, 94),
                Icon = bcicon,
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.None,
            };


            System.Windows.Forms.Label label = new System.Windows.Forms.Label
            {
                Text = Text,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.None,
                Width = StartupForm.Width - 10, 
                Height = 100,
                Font = new Font("Calibri", 18),
                ForeColor = Color.White,
            };

            label.Left = (StartupForm.Width - label.Width) / 2;
            label.Top = (StartupForm.Height - label.Height) / 2;


            StartupForm.Controls.Add(label);
            
            return StartupForm;
        }

        static void CloseForm(Form Form)
        {
            Form.Invoke((MethodInvoker)delegate
            {
                Form.Close();
            });
        }

        //returns the process count based on a string process name
        static int GetProcessCount(string processName)
        {
            Process[] processes = Process.GetProcessesByName(processName);

            return processes.Length;
        }
        static void BCStartup(Form StartupForm)
        {              
            string navServiceName = "MicrosoftDynamicsNavServer$LSPOS";

            if (WaitForServiceToStart(navServiceName))
                StartAppShell();

            else
            {
                MessageBox.Show("Timeout exceeded for " + navServiceName);
                System.Windows.Forms.Application.Exit();
            }

            CloseForm(StartupForm);
          
        }

        static bool WaitForServiceToStart(string serviceName)
        {
            bool flag = false;          
            DateTime startTime = DateTime.Now;

            while (!flag)
            {
                ServiceController serviceController = new ServiceController(serviceName);

                try
                {
                    ServiceControllerStatus serviceStatus = serviceController.Status;

                    //ff the service is running, return true
                    if (serviceStatus == ServiceControllerStatus.Running)                   
                       return true;
                    
                }
                catch (InvalidOperationException ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }

                //check if the elapsed time has exceeded the timeout 
                if ((DateTime.Now - startTime).TotalSeconds > 1000)                                 
                    return false;
                              
                Thread.Sleep(1000);
            }

            return false; 
        }
     

        static void StartAppShell()
        {
            //Double check that appshell still isn't running

            if ((GetProcessCount("LSAppShell.Desktop.Reunion") == 0))
            {              
                string appCommand = "shell:AppsFolder\\LSRETAILINC.LSCentral_kxgjq7wq1nzrp!App";

                // Start the process
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = appCommand,
                    UseShellExecute = true
                });

                //wait for appshell to launch
                while (GetProcessCount("LSAppShell.Desktop.Reunion") == 0)
                {
                    Thread.Sleep(100);
                }
            }
           
        }

        static bool IsServiceRunning(string serviceName) {
            try {
                ServiceController serviceController = new ServiceController(serviceName);
                ServiceControllerStatus serviceStatus = serviceController.Status;

                //ff the service is running, return true
                if (serviceStatus == ServiceControllerStatus.Running) {
                    return true;
                }

            }
            catch (InvalidOperationException ex) {
                Console.WriteLine($"Error: {ex.Message}");
            }

            return false;
        }

        //handler for when form closes
        private static void OnApplicationExit(object sender, EventArgs e) {
            if (mutex != null) {
                mutex.ReleaseMutex();
                mutex = null;
            }
        }
    }
}