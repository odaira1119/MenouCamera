using System.Windows;
using MenouCamera.Models.Services.Abstractions;
using MenouCamera.Models.Services.Implementations;
using MenouCamera.ViewModels;

namespace MenouCamera.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            ICameraService Camera = new OpenCvCameraService();
            IThumbnailService Thumb = new OpenCvThumbnailService();
            IImageStorageService Storage = new FileSystemImageStorageService();
            IFileDialogService Dialog = new WpfFileDialogService();

            this.DataContext = new MainViewModel(Camera, Thumb, Storage, Dialog);
        }
    }
}
