using System.Text;
using Microsoft.AspNetCore.Identity;

namespace VendorRadar.Web;

internal static class AccountPasswordCommand
{
    public static int Run()
    {
        var password = Console.IsInputRedirected ? Console.ReadLine() : ReadPassword();
        if (password is not { Length: >= 12 and <= 256 })
        {
            Console.Error.WriteLine("Use a password between 12 and 256 characters.");
            return 1;
        }
        Console.WriteLine(new PasswordHasher<string>().HashPassword("VendorRadar", password));
        return 0;
    }

    private static string ReadPassword()
    {
        Console.Error.Write("Password: ");
        var password = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace && password.Length > 0) password.Length--;
            else if (!char.IsControl(key.KeyChar)) password.Append(key.KeyChar);
        }
        Console.Error.WriteLine();
        return password.ToString();
    }
}
