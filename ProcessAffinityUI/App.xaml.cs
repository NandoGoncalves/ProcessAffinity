using ProcessAffinityUI.Threading;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;

namespace ProcessAffinityUI
{

    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {

            if (e.Args.Length > 0)
            {
                string computerName = string.Empty;
                string domain = string.Empty;
                string user = string.Empty;
                string password = string.Empty;
                int core = 0;

                foreach (var a in e.Args)
                {
                    switch (a.ToUpper().Substring(0, a.IndexOf(':')))
                    {
                        case "COMPUTER":
                            computerName = a.ToUpper().Substring(a.IndexOf(':') + 1);
                            break;
                        case "C":
                            computerName = a.ToUpper().Substring(a.IndexOf(':') + 1);
                            break;
                        case "DOMAIN":
                            domain = a.ToUpper().Substring(a.IndexOf(':') + 1);
                            break;
                        case "D":
                            domain = a.ToUpper().Substring(a.IndexOf(':') + 1);
                            break;
                        case "USER":
                            user = a.ToUpper().Substring(a.IndexOf(':') + 1);
                            break;
                        case "U":
                            user = a.ToUpper().Substring(a.IndexOf(':') + 1);
                            break;
                        case "PASSWORD":
                            password = a.ToUpper().Substring(a.IndexOf(':') + 1);
                            break;
                        case "P":
                            password = a.ToUpper().Substring(a.IndexOf(':') + 1);
                            break;
                        case "CORE":
                            core = int.Parse(a.ToUpper().Substring(a.IndexOf(':') + 1));
                            break;
                    }
                }

                if (!string.IsNullOrEmpty(computerName) & !string.IsNullOrEmpty(domain) & !string.IsNullOrEmpty(user) & !string.IsNullOrEmpty(password))
                {
                    Processes processes = new Processes(computerName, domain, user, password);

                    List<ProcessUserControl> processUserControls = new List<ProcessUserControl>();

                    foreach (var p in processes)
                    {
                        processUserControls.Add(new ProcessUserControl(p));
                    }

                    ProcessAffinityWindow paw = new ProcessAffinityWindow(processUserControls);

                    if (core > -1)
                    {
                        paw.SetCPUCheckBoxesUnchecked();
                        paw.SetCPUCheckBox(core, true);
                    }
                    else
                    {
                        paw.SetCPUCheckBoxesChecked();
                    }

                    paw.SetProcessorsAffinities();
                    paw = null;
                }

                Environment.Exit(0);
            }
        }
    }
}
