namespace NovaVox.Core.Text;

/// <summary>
/// Port de <c>difflib.SequenceMatcher</c> (algorithme de Ratcliff/Obershelp)
/// limité à ce qui est utilisé par NovaVox : <see cref="Ratio"/>. Reproduit
/// fidèlement l'algorithme de CPython (y compris l'heuristique "autojunk")
/// pour que la correspondance floue des commandes vocales se comporte
/// exactement comme dans l'application Python.
/// </summary>
public sealed class SequenceMatcher
{
    private readonly string _a;
    private readonly string _b;
    private readonly Dictionary<char, List<int>> _b2j = new();

    public SequenceMatcher(string a, string b)
    {
        _a = a;
        _b = b;
        BuildB2J();
    }

    private void BuildB2J()
    {
        for (int i = 0; i < _b.Length; i++)
        {
            char c = _b[i];
            if (!_b2j.TryGetValue(c, out var list))
            {
                list = new List<int>();
                _b2j[c] = list;
            }
            list.Add(i);
        }

        int n = _b.Length;
        if (n >= 200)
        {
            int ntest = n / 100 + 1;
            var popular = _b2j.Where(kv => kv.Value.Count > ntest).Select(kv => kv.Key).ToList();
            foreach (var c in popular)
            {
                _b2j.Remove(c);
            }
        }
    }

    private (int I, int J, int K) FindLongestMatch(int alo, int ahi, int blo, int bhi)
    {
        int besti = alo, bestj = blo, bestsize = 0;
        var j2len = new Dictionary<int, int>();
        for (int i = alo; i < ahi; i++)
        {
            var newj2len = new Dictionary<int, int>();
            if (_b2j.TryGetValue(_a[i], out var js))
            {
                foreach (var j in js)
                {
                    if (j < blo) continue;
                    if (j >= bhi) break;
                    int k = (j2len.TryGetValue(j - 1, out var v) ? v : 0) + 1;
                    newj2len[j] = k;
                    if (k > bestsize)
                    {
                        besti = i - k + 1;
                        bestj = j - k + 1;
                        bestsize = k;
                    }
                }
            }
            j2len = newj2len;
        }
        return (besti, bestj, bestsize);
    }

    private List<(int A, int B, int Size)> GetMatchingBlocks()
    {
        int la = _a.Length, lb = _b.Length;
        var queue = new Stack<(int Alo, int Ahi, int Blo, int Bhi)>();
        queue.Push((0, la, 0, lb));
        var rawBlocks = new List<(int A, int B, int Size)>();
        while (queue.Count > 0)
        {
            var (alo, ahi, blo, bhi) = queue.Pop();
            var (i, j, k) = FindLongestMatch(alo, ahi, blo, bhi);
            if (k > 0)
            {
                rawBlocks.Add((i, j, k));
                if (alo < i && blo < j)
                    queue.Push((alo, i, blo, j));
                if (i + k < ahi && j + k < bhi)
                    queue.Push((i + k, ahi, j + k, bhi));
            }
        }
        rawBlocks.Sort((x, y) => x.A != y.A ? x.A.CompareTo(y.A) : x.B.CompareTo(y.B));

        var merged = new List<(int A, int B, int Size)>();
        int i1 = 0, j1 = 0, k1 = 0;
        foreach (var (i2, j2, k2) in rawBlocks)
        {
            if (i1 + k1 == i2 && j1 + k1 == j2)
            {
                k1 += k2;
            }
            else
            {
                if (k1 > 0) merged.Add((i1, j1, k1));
                i1 = i2; j1 = j2; k1 = k2;
            }
        }
        if (k1 > 0) merged.Add((i1, j1, k1));
        merged.Add((la, lb, 0));
        return merged;
    }

    public double Ratio()
    {
        int matches = GetMatchingBlocks().Sum(b => b.Size);
        int total = _a.Length + _b.Length;
        return total == 0 ? 1.0 : 2.0 * matches / total;
    }
}
