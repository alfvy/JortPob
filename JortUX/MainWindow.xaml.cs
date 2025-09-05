using JortPob;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace JortUX
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public Thread job, log;
        public bool running;
        public MainWindow()
        {
            InitializeComponent();

            running = true;
            job = new(Run);
            log = new(Check);


            job.Start();
            log.Start();
        }

        public void Check()
        {
            while (running)
            {
                if (JortPob.Common.Lort.update)
                {
                    this.Dispatcher.Invoke(() =>
                    {
                        ReRender();
                    });
                }
                Thread.Yield();
            }
        }

        //public void ReRender()
        //{
        //    TextBlock main = (TextBlock)FindName("MainOutput");
        //    TextBlock debug = (TextBlock)FindName("DebugOutput");
        //    TextBlock progress = (TextBlock)FindName("ProgressOutput");
        //    ProgressBar bar = (ProgressBar)FindName("ProgressBar");
        //    string mainText = "", debugText = "";
        //    foreach(string line in JortPob.Common.Lort.mainOutput)
        //    {
        //        mainText = line + "\n" + mainText;
        //    }
        //    foreach(string line in JortPob.Common.Lort.debugOutput)
        //    {
        //        debugText = line + "\n" + debugText;
        //    }

        //    main.Text = mainText;
        //    debug.Text = debugText;
        //    progress.Text = $"{JortPob.Common.Lort.progressOutput} [ {JortPob.Common.Lort.current} / {JortPob.Common.Lort.total} ]";
        //    float p = Math.Max(0, Math.Min(1, ((float)JortPob.Common.Lort.current / (float)JortPob.Common.Lort.total))) * 100f;
        //    if (float.IsNaN(p)) { p = 0; }
        //    bar.Value = p;

        //    JortPob.Common.Lort.update = false;
        //}

        public void ReRender()
        {
            TextBlock main = (TextBlock)FindName("MainOutput");
            TextBlock debug = (TextBlock)FindName("DebugOutput");
            TextBlock progress = (TextBlock)FindName("ProgressOutput");
            ProgressBar bar = (ProgressBar)FindName("ProgressBar");

            string mainText = "", debugText = "";

            // top-to-bottom order
            foreach (string line in JortPob.Common.Lort.mainOutput)
                mainText += line + "\n";

            foreach (string line in JortPob.Common.Lort.debugOutput)
                debugText += line + "\n";

            main.Text = mainText;
            debug.Text = debugText;
            progress.Text = $"{JortPob.Common.Lort.progressOutput} [ {JortPob.Common.Lort.current} / {JortPob.Common.Lort.total} ]";

            float p = Math.Max(0, Math.Min(1, ((float)JortPob.Common.Lort.current / (float)JortPob.Common.Lort.total))) * 100f;
            if (float.IsNaN(p)) p = 0;
            bar.Value = p;

            JortPob.Common.Lort.update = false;
        }

        public void Run()
        {
            Main.Convert();
            running = false;
        }

        private void OnClose(object sender, CancelEventArgs e)
        {
            if (job.IsAlive)
            {
                e.Cancel = true; // Prevent closing window. The job thread can't be stopped easily so guh. Use debug terminate to kill program.
            }
        }
    }
}