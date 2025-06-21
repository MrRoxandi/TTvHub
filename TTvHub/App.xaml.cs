namespace TTvHub
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new MainPage()) { 
                Title = "TTvHub", 
                Height=650, 
                Width=1400
            };
        }
    }
}
