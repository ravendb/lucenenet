using Lucene.Net.Util;

namespace Lucene.Net.Index;

public class UnmanagedTerm
{
    private readonly string field;
    private readonly UnmanagedStringArray.UnmanagedString unmanagedString;

    public string Field => field;

    public UnmanagedStringArray.UnmanagedString Text => unmanagedString;

    public UnmanagedTerm(string field, UnmanagedStringArray.UnmanagedString unmanagedString)
    {
        this.field = field;
        this.unmanagedString = unmanagedString;
    }

    public Term ToTerm()
    {
        return new Term(field, unmanagedString.ToString(), false);
    }
}