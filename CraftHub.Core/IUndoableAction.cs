namespace CraftHub.Core;

public interface IUndoableAction
{
    string Description { get; }
    void Undo();
    void Redo();
}
