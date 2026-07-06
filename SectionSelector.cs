namespace PS5MemoryPeeker;

public static class SectionSelector
{
    public static void ScoreSections(IEnumerable<MemorySection> sections)
    {
        foreach (MemorySection section in sections)
        {
            section.SelectionScore = ScoreSection(section);
            section.Kind = ClassifySection(section);
            section.IsSelected = section.SelectionScore >= 45;
        }
    }

    private static int ScoreSection(MemorySection section)
    {
        string name = section.Name.ToLowerInvariant();
        int score = 20;

        if (name.Contains("eboot") || name.Contains("/app0/"))
        {
            score += 60;
        }

        if (name.Contains("anon") || name.Contains("dlmalloc") || name.Contains("heap") || name.Contains("game"))
        {
            score += 55;
        }

        if ((section.Protection & 0x2) == 0x2)
        {
            score += 25;
        }

        if ((section.Protection & 0x4) == 0x4)
        {
            score -= 20;
        }

        if (LooksLikeLibraryOrPayload(name))
        {
            score -= 90;
        }

        if (section.ByteLength < 4096)
        {
            score -= 15;
        }

        if (section.ByteLength > 1024UL * 1024UL * 1024UL)
        {
            score -= 20;
        }

        return Math.Clamp(score, 0, 100);
    }

    private static string ClassifySection(MemorySection section)
    {
        string name = section.Name.ToLowerInvariant();
        if (name.Contains("eboot") || name.Contains("/app0/"))
        {
            return "Game image";
        }

        if (name.Contains("anon") || name.Contains("dlmalloc") || name.Contains("heap") || name.Contains("game"))
        {
            return "Game heap";
        }

        if (LooksLikeLibraryOrPayload(name))
        {
            return "Library";
        }

        if ((section.Protection & 0x2) == 0x2)
        {
            return "Writable";
        }

        return "Mapped";
    }

    private static bool LooksLikeLibraryOrPayload(string lower)
    {
        return lower.Contains(".sprx")
            || lower.Contains(".prx")
            || lower.Contains(".so")
            || lower.Contains(".elf")
            || lower.Contains("/lib")
            || lower.Contains("\\lib")
            || lower.Contains("libkernel")
            || lower.Contains("libsce");
    }
}
