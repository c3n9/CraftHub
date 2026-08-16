namespace CraftHub.Formulas.Sidecar;

/// <summary>The sidecar file's JSON is either not valid JSON, or valid JSON that isn't shaped like
/// a sidecar (missing "target", wrong types, ...). Callers catch this specifically to trigger the
/// "corrupt sidecar" flow — open the document anyway, warn, rename the sidecar to .bak — rather
/// than losing the file or refusing to open the document.</summary>
public sealed class FormulaSidecarFormatException(string message) : System.Exception(message);
