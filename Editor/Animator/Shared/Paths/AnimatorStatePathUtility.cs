using System;

namespace MVA.Toolbox.AnimatorShared.Paths
{
    internal static class AnimatorStatePathUtility
    {
        private const char SlashEscapeChar = '\u001F';

        internal const string SubStateMachineSuffix = " (子状态机)";
        internal const string DisplayPathSeparator = " > ";

        internal static string CombinePath(string parentPath, string segment)
        {
            string encodedSegment = EncodeSegment(segment);
            return string.IsNullOrEmpty(parentPath) ? encodedSegment : parentPath + "/" + encodedSegment;
        }

        internal static string EncodeSegment(string segment)
        {
            return string.IsNullOrEmpty(segment) ? string.Empty : segment.Replace('/', SlashEscapeChar);
        }

        internal static string DecodeSegment(string segment)
        {
            return string.IsNullOrEmpty(segment) ? string.Empty : segment.Replace(SlashEscapeChar, '/');
        }

        internal static string[] SplitPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return Array.Empty<string>();
            }

            string[] rawSegments = path.Split('/');
            string[] result = new string[rawSegments.Length];
            for (int i = 0; i < rawSegments.Length; i++)
            {
                result[i] = DecodeSegment(rawSegments[i]);
            }

            return result;
        }
    }
}
