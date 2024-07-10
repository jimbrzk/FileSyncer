namespace FileSyncer.Cli.Extensions
{
    public static class ArgumentsExtensions
    {
        public static Dictionary<string, string> GetArgumets(this string[] args)
        {
            var dict = new Dictionary<string, string>();

            foreach (var arg in args.Where(x => x.StartsWith("--")))
            {
                var argPos = Array.IndexOf(args, arg);
                var val = ((args.Length - 1) > argPos) ? ((args[argPos + 1].StartsWith("--")) ? string.Empty : args[argPos + 1]) : string.Empty;
                dict.TryAdd(arg.Replace("--", string.Empty), val);
            }

            return dict;
        }
    }
}