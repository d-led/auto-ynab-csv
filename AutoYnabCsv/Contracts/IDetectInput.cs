namespace AutoYnabCsv.Contracts;

public interface IDetectInput
{
    Detection TryDetect(string input);
}
