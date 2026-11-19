namespace Aether;

internal static class Program
{
    private static async Task<int> Main()
    {
        try
        {
            await new App().RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Term.Warn(ex.Message);
            Input.Pause();
            return 1;
        }
    }
}
