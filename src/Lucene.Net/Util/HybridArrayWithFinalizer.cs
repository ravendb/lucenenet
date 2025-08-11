using System;

namespace Lucene.Net.Util;

public class HybridArrayWithFinalizer<T> : HybridArray<T> where T : unmanaged
{
    public HybridArrayWithFinalizer(int length, UnmanagedStringArray.Type type, bool clear) : base(length, type, clear)
    {
    }

    public override void Dispose()
    {
        GC.SuppressFinalize(this);
        base.Dispose();
    }

    ~HybridArrayWithFinalizer()
    {
        Dispose();
    }
}