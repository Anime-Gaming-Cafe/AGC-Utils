public class ConsoleProgressBar
{
    private readonly int _barSize;
    private readonly char _progressCharacter;
    private readonly int _startCursorTop;
    private readonly int _total;
    private readonly bool _enabled;
    private int _current;

    public ConsoleProgressBar(int total, int barSize = 50, char progressCharacter = '#')
    {
        _total = total;
        _current = 0;
        _barSize = barSize;
        _progressCharacter = progressCharacter;

        // When stdout is redirected or there is no real console buffer (container, systemd,
        // nohup, piped output) the cursor APIs throw "IOException: The handle is invalid".
        // In that case we disable the visual bar so startup does not crash.
        try
        {
            if (Console.IsOutputRedirected)
            {
                _enabled = false;
                return;
            }

            _startCursorTop = Console.CursorTop;
            _enabled = true;
        }
        catch (IOException)
        {
            _enabled = false;
        }
    }

    public void Increment()
    {
        _current++;
        if (_enabled)
            Draw();
    }

    private void Draw()
    {
        Console.SetCursorPosition(0, _startCursorTop);

        if (_current < _total)
        {
            var progress = (float)_current / _total;
            var progressWidth = (int)(_barSize * progress);

            Console.Write("[");
            Console.Write(new string(_progressCharacter, progressWidth));
            Console.Write(new string('-', _barSize - progressWidth));
            Console.Write("] ");

            var percentage = (int)(progress * 100);
            Console.Write($"{percentage}% Completed");
        }
        else
        {
            Console.Write(new string(' ', Console.WindowWidth));
            Console.SetCursorPosition(0, _startCursorTop);
        }
    }
}