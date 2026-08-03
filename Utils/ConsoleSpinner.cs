public class ConsoleSpinner
{
    private readonly Task _task;
    private readonly bool _enabled;
    private bool _active;
    private int _counter;

    public ConsoleSpinner()
    {
        _enabled = !Console.IsOutputRedirected;
        _task = Task.Run(() => Spin());
    }

    public void Start()
    {
        _active = true;
    }

    public void Stop()
    {
        _active = false;
        Console.Write("");
        _task.Wait();
    }

    private void Spin()
    {
        if (!_enabled)
            return;

        while (_active)
        {
            switch (_counter % 4)
            {
                case 0:
                    Console.Write("/");
                    break;
                case 1:
                    Console.Write("-");
                    break;
                case 2:
                    Console.Write("\\");
                    break;
                case 3:
                    Console.Write("|");
                    break;
            }

            Console.SetCursorPosition(Console.CursorLeft - 1, Console.CursorTop);
            _counter++;
            Thread.Sleep(25);
        }
    }
}