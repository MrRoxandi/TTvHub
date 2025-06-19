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
                MaximumHeight=650,  
                MaximumWidth=1400,
                Height=650, 
                Width=1400
            };
        }
    }
}
