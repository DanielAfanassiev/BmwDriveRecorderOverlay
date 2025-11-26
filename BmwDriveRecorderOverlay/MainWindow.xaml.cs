using OpenCvSharp;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Forms;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;
using MessageBox = System.Windows.MessageBox;
using Point = OpenCvSharp.Point;
using Size = OpenCvSharp.Size;
using Window = System.Windows.Window;

namespace BmwDriveRecorderOverlay
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private Visibility _processing = Visibility.Visible;
        public Visibility Processing
        {
            get => _processing;
            set
            {
                _processing = value;
                OnPropertyChanged(nameof(Processing));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        private string selectedFolder = "";

        private void SelectFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                selectedFolder = dialog.SelectedPath;
                FolderPathText.Text = selectedFolder;
            }
        }

        private async void ProcessVideo_Click(object sender, RoutedEventArgs e)
        {
            Processing = Visibility.Hidden;
            var files = Directory.EnumerateFiles(selectedFolder);

            string videoFile = "";
            string metadataFile = "";

            foreach (var file in files)
            {
                if (file.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
                    videoFile = await ConvertTsToMp4(file);
                else if (file.EndsWith("Metadata.json", StringComparison.OrdinalIgnoreCase))
                    metadataFile = file;
            }

            if (string.IsNullOrEmpty(videoFile) || string.IsNullOrEmpty(metadataFile))
            {
                MessageBox.Show("Required files not found in the selected folder.");
                return;
            }

            // Open video
            using var capture = new VideoCapture(videoFile);

            // Load metadata
            string json = File.ReadAllText(metadataFile);
            var vehicles = JsonSerializer.Deserialize<List<VehicleData>>(json);
            if (vehicles == null || vehicles.Count == 0) return;

            var metadataEntries = vehicles[0].Entries;
            if (metadataEntries is null) return;
            int ratio = (int)(metadataEntries.Count / capture.FrameCount);

            // VideoWriter setup
            int width = (int)capture.FrameWidth;
            int height = (int)capture.FrameHeight;
            string folder = selectedFolder + "";

            const char backSlash = '\\';

            int lastSlash = folder.LastIndexOf(backSlash);

            string fileName = folder.Substring(lastSlash + 1) + "-" + DateTime.UtcNow.ToFileTime().ToString() + ".mp4";
            string outputFolder = string.Concat(folder.AsSpan(0, lastSlash), "\\ProcessedVideos\\");

            var output = Directory.CreateDirectory(outputFolder);

            string outputFile = Path.Combine(outputFolder, fileName);

            using var writer = new VideoWriter(outputFile, FourCC.MP4V, capture.Fps, new Size(width, height), true);

            // Text overlay setup
            HersheyFonts font = HersheyFonts.HersheySimplex;
            double fontScale = 1.0;
            int thickness = 2;
            int baseline;
            Size textSize = Cv2.GetTextSize("Speed: 000 km/h", font, fontScale, thickness, out baseline);

            int x = 10;
            int y = textSize.Height + 10;

            // Process frames and overlay text
            var frame = new Mat();
            int frameCount = 0;


            // Data tracking
            string? startTime = "";
            var lastStillIndex = -1;
            double topSpeed = 0;
            double topSpeedIndex = -1;

            foreach (var item in metadataEntries)
            {
                if (item.VelocityKmH > topSpeed) // "Calculating" top speed
                {
                    topSpeed = item.VelocityKmH;
                    topSpeedIndex = item.Id-1;
                }
            }

            double timeDif = -1;

            while (capture.Read(frame)) // Drawing each frame and drawing information on frames
            {
                int index = Math.Min(frameCount * ratio, metadataEntries.Count - 1);
                var entry = metadataEntries[index];
                string velocityKph = PadWithZeros(entry.VelocityKmH);

                List<string> lines = [$"Speed: {velocityKph} km/h", $"{entry.VelocityMpH} mph"];

                if (index > topSpeedIndex)
                {
                    var difference = (timeDif / 30);
                    lines.Add($"Top speed: {topSpeed}km / h");
                    lines.Add($"{metadataEntries[lastStillIndex].VelocityKmH} to {metadataEntries[(int)topSpeedIndex].VelocityKmH}: " + Math.Round(difference, 3) + "s");

                }

                if (entry.VelocityKmH != 0 && lastStillIndex == -1)
                {
                    lastStillIndex = Math.Max(0, (entry.Id-1) - 1); // index of array where first movement occured (entry.Id-1), and then go back a frame because  we want the previous one (-1)
                    startTime = metadataEntries[lastStillIndex].Time;
                    timeDif = topSpeedIndex - (lastStillIndex - 2);

                }



                for (int i = 0; i < lines.Count; i++)
                {
                    Cv2.PutText(frame, lines[i], new Point(x, y * (i+1)), font, fontScale, new Scalar(0, 0, 255), thickness);
                }
                writer.Write(frame);

                frameCount++;
            }

            MessageBox.Show("Processing complete. Output saved to " + outputFile);
            Processing = Visibility.Visible;
            File.Delete(videoFile);
        }



        private async Task<string> ConvertTsToMp4(string tsFilePath)
        {
            if (string.IsNullOrEmpty(tsFilePath) || !File.Exists(tsFilePath))
                throw new FileNotFoundException("Input .ts file not found", tsFilePath);

            // Ensure FFmpeg executables are downloaded
            await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official);

            string? directory = Path.GetDirectoryName(tsFilePath);
            if (string.IsNullOrEmpty(directory))
                throw new ArgumentException("Could not determine directory for the input file.", nameof(tsFilePath));

            string outputFile = Path.Combine(
                directory,
                Path.GetFileNameWithoutExtension(tsFilePath) + ".mp4"
            );

            var conversion = await FFmpeg.Conversions.New()
                .AddParameter($"-i \"{tsFilePath}\" -c copy \"{outputFile}\"")
                .Start();

            return outputFile;
        }

        private string PadWithZeros(double speed)
        {
            string speedString = "";
            if (speed < 1)
            {
                speedString = "  0.00";
            }
            else if (speed < 10)
            {
                speedString = "  " + speed.ToString();
            }
            else if (speed < 100)
            {
                speedString = " " + speed.ToString();
            }
            else
            {
                speedString = speed.ToString();
            }
            while (speedString.Length < 6)
            {
                if (speedString.Length == 3)
                {
                    speedString += ".";
                }
                else
                {
                    speedString += "0";
                }
            }
            return speedString;
        }
    }

    public class Entry
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("date")]
        public string? Date { get; set; }

        [JsonPropertyName("time")]
        public string? Time { get; set; }

        [JsonPropertyName("latitude")]
        public double Latitude { get; set; }

        [JsonPropertyName("longitude")]
        public double Longitude { get; set; }

        [JsonPropertyName("velocity_KM/H")]
        public double VelocityKmH { get; set; }

        [JsonPropertyName("velocity_MP/H")]
        public string? VelocityMpH { get; set; }
    }

    public class VehicleData
    {
        [JsonPropertyName("VIN")]
        public string? VIN { get; set; }

        [JsonPropertyName("entries")]
        public List<Entry>? Entries { get; set; }
    }
}

