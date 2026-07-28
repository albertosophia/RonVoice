namespace RonVoice.Core.Input;

public interface IInputSender
{
    void Send(KeySequence sequence, CancellationToken ct = default);
}
