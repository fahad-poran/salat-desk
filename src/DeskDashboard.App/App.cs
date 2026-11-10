namespace DeskDashboard.App;

public sealed class App(MainPage mainPage) : Application
{
    protected override Window CreateWindow(IActivationState? activationState) =>
        new(mainPage) { Title = "Salat Desk" };
}
