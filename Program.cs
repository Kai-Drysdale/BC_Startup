using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BC_Startup {
    internal static class Program {

        static readonly Mutex mutex = new Mutex(false, "{E8F5859B-B9F5-475B-B73A-2567D14A6C16}");
        static readonly string navServiceName = "MicrosoftDynamicsNavServer$LSPOS";
        //static readonly string navServiceName = "spooler";

        [STAThread]
        static void Main()
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, e) => {
                Exception ex = (Exception)e.ExceptionObject;
                File.AppendAllText("crashlog.txt", ex.ToString());
            };

            bool acquired = false;

            try {
                acquired = mutex.WaitOne(0);
            } catch (AbandonedMutexException) {
                //previous owner terminated without releasing the mutex
                //the current thread now owns it
                acquired = true;
            }
            //another instance is already running, exit
            if (!acquired) {
                return;
            }

            try {
                //if the NAV service is running, launch AppShell immediately
                if (IsServiceRunning(navServiceName)) {
                    StartAppShell();
                    return;
                }

                //otherwise, display the startup form and wait
                InitializeStartupForm();
            }
            finally {
                //release ownership when finished
                mutex.ReleaseMutex();
                mutex.Dispose();
            }
        }

        static void InitializeStartupForm()
        {
            Form startupForm = CreateForm( "BC Service is Starting, Please Wait ...");

            startupForm.Shown += (sender, e) => {
                Task.Run(() => BCStartup(startupForm));
            };

            startupForm.ShowDialog();
        }

        static Form CreateForm(string Text)
        {
            Icon bcicon;
            try {
                bcicon = new Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BC.ico"));
            } catch (FileNotFoundException) {
                bcicon = SystemIcons.Information;  
            }

            Form StartupForm = new Form { 
                Text = "BC Startup",
                Width = 450,
                Height = 120,
                BackColor = Color.FromArgb(52, 30, 94),
                Icon = bcicon,
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.None,
            };

            System.Windows.Forms.Label label = new System.Windows.Forms.Label {
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
            Form.Invoke((MethodInvoker)delegate {
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

            else {
                MessageBox.Show("Timeout exceeded for " + navServiceName);
                System.Windows.Forms.Application.Exit();
            }

            CloseForm(StartupForm);
          
        }

        static bool WaitForServiceToStart(string serviceName)
        {
            bool flag = false;          
            DateTime startTime = DateTime.Now;

            while (!flag) {
                ServiceController serviceController = new ServiceController(serviceName);

                try {
                    ServiceControllerStatus serviceStatus = serviceController.Status;

                    //ff the service is running, return true
                    if (serviceStatus == ServiceControllerStatus.Running)                   
                       return true;
                    
                } catch (InvalidOperationException ex) {
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
            const string processName = "LSAppShell.Desktop.Reunion";
            const string appCommand = @"shell:AppsFolder\LSRETAILINC.LSCentral_kxgjq7wq1nzrp!App";

            if (GetProcessCount(processName) > 0)
                return;

            for (int attempt = 0; attempt < 2; attempt++) {
                Process.Start(new ProcessStartInfo
                {
                    FileName = appCommand,
                    UseShellExecute = true
                });

                DateTime startTime = DateTime.Now;

                while (GetProcessCount(processName) == 0 && (DateTime.Now - startTime).TotalSeconds < 10) {
                    Thread.Sleep(100);
                }

                // AppShell started successfully
                if (GetProcessCount(processName) > 0)
                    return;

                // Only attempt repair after the first failed launch
                if (attempt == 0) {
                    if (!TryToRepairAppShell())
                        break;
                }
            }

            MessageBox.Show("AppShell failed to start", "BC Startup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Exit();
        }

        static bool TryToRepairAppShell()
        {
            string command =
                "$ErrorActionPreference = 'Stop'; " +
                "$package = Get-AppxPackage -Name 'LSRETAILINC.LSCentral'; " +
                "if (-not $package) { throw 'LS Central not found' }; " +
                "$manifest = Join-Path $package.InstallLocation 'AppxManifest.xml'; " +
                "Add-AppxPackage -DisableDevelopmentMode -Register $manifest -ErrorAction Stop";

            string encodedCommand = Convert.ToBase64String(
                Encoding.Unicode.GetBytes(command)
            );

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -EncodedCommand " +
                            encodedCommand,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            try
            {
                using (var process = Process.Start(startInfo))
                {

                    // Read both streams concurrently.
                    Task<string> outputTask =
                        process.StandardOutput.ReadToEndAsync();

                    Task<string> errorTask =
                        process.StandardError.ReadToEndAsync();

                    process.WaitForExit();

                    string output = outputTask.GetAwaiter().GetResult();
                    string errors = errorTask.GetAwaiter().GetResult();

                    if (process.ExitCode != 0)
                    {
                        LogError("LS Central repair failed: " + errors);
                        return false;
                    }

                    LogError("LS Central repair completed successfully.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogError("LS Central repair exception: " + ex);
                return false;
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

            } catch (InvalidOperationException ex) {
                LogError($"Error: {ex.Message}");
            }

            return false;
        }

        static void LogError(string message)
        {
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BC_Startup.log");

            File.AppendAllText(
                logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}"
            );
        }
    }
}