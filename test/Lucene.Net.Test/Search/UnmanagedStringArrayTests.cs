using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using Lucene.Net.Index;
using Lucene.Net.Util;
using NUnit.Framework;
using static Lucene.Net.Util.UnmanagedStringArray;

namespace Lucene.Net.Search
{
    public class UnmanagedStringArrayTests
    {
        [Test]
        public void Should_Find_All_Terms()
        {
            var terms = new UnmanagedStringArray(11, startIndex: 1);

            for (var letter = 'a'; letter <= 'j'; letter++)
            {
                terms.Add(new Span<char>(letter.ToString().ToCharArray()));
            }

            for (var i = 1; i < terms.Length; i++)
            {
                var position = FieldComparator.BinarySearch(terms, terms[i]);
                Assert.AreEqual(i, position);
            }

            VerifyNonExistingTerms(terms);
        }

        [Test]
        public void Should_Find_With_Missing_Terms()
        {
            var terms = new UnmanagedStringArray(11, startIndex: 1);

            var count = 0;
            for (var letter = 'a'; letter <= 'j'; letter++)
            {
                if (++count == 5)
                {
                    var termBuffer = new TermBuffer();
                    termBuffer.Set(new Term("test", letter.ToString()));
                    terms.AddDeleted(termBuffer);
                }
                else
                {
                    terms.Add(new Span<char>(letter.ToString().ToCharArray()));
                }
            }

            for (var i = 1; i < terms.Length; i++)
            {
                if (i == 5)
                    continue;

                var position = FieldComparator.BinarySearch(terms, terms[i]);
                // in position 5 we save the same value as in position 4
                var expected = i == 4 ? 5 : i;
                Assert.AreEqual(expected, position);
            }

            VerifyNonExistingTerms(terms);

            terms = new UnmanagedStringArray(11, startIndex: 1);

            for (var letter = 'a'; letter <= 'j'; letter++)
            {
                if (letter != 'j')
                {
                    var termBuffer = new TermBuffer();
                    termBuffer.Set(new Term("test", letter.ToString()));
                    terms.AddDeleted(termBuffer);
                }
                else
                {
                    terms.Add(new Span<char>(letter.ToString().ToCharArray()));
                }
            }

            for (var i = 1; i < terms.Length; i++)
            {
                if (i != 10)
                    continue;

                var position = FieldComparator.BinarySearch(terms, terms[i]);
                Assert.AreEqual(10, position);
            }

            VerifyNonExistingTerms(terms);

            terms = new UnmanagedStringArray(11, startIndex: 1);

            for (var letter = 'a'; letter <= 'j'; letter++)
            {
                if (letter != 'a' && letter != 'j')
                {
                    var termBuffer = new TermBuffer();
                    termBuffer.Set(new Term("test", letter.ToString()));
                    terms.AddDeleted(termBuffer);
                }
                else
                {
                    terms.Add(new Span<char>(letter.ToString().ToCharArray()));
                }
            }

            for (var i = 1; i < terms.Length; i++)
            {
                var position = FieldComparator.BinarySearch(terms, terms[i]);

                switch (i)
                {
                    case 1:
                        Assert.AreEqual(5, position);
                        break;
                    case 10:
                        Assert.AreEqual(i, position);
                        break;
                }
            }

            VerifyNonExistingTerms(terms);
        }

        [Test]
        public void Should_Find_Terms()
        {
            var terms = new UnmanagedStringArray(char.MaxValue + 1, startIndex: 0);
            for (int code = char.MinValue; code <= char.MaxValue; code++)
            {
                char letter = (char)code;
                terms.Add(new Span<char>([letter]));
            }

            for (var i = 0; i < terms.Length; i++)
            {
                var position = FieldComparator.BinarySearch(terms, terms[i]);
                Assert.AreEqual(i, position);
            }
        }

        [Test]
        public void Should_Find_Terms_Random_Ascii_Strings()
        {
            var uniqueStrings = new HashSet<string>();
            var random = new Random(Seed: 3117);

            const int asciiStart = 0;
            const int asciiEnd = 127;

            while (uniqueStrings.Count < 64 * 1024)
            {
                var length = random.Next(1, 101);

                var chars = new char[length];
                for (var j = 0; j < length; j++)
                {
                    chars[j] = (char)random.Next(asciiStart, asciiEnd + 1);
                }

                uniqueStrings.Add(new string(chars));
            }

            var sortedStrings = new List<string>(uniqueStrings);
            sortedStrings.Sort(string.CompareOrdinal);

            var terms = new UnmanagedStringArray(sortedStrings.Count + 1, startIndex: 0);
            foreach (var str in sortedStrings)
            {
                terms.Add(new Span<char>(str.ToCharArray()));
            }

            for (var i = 0; i < terms.Length; i++)
            {
                var position = FieldComparator.BinarySearch(terms, terms[i]);
                Assert.AreEqual(i, position);

                Assert.AreEqual(sortedStrings[i], terms[i].ToString());
            }
        }

        [Test]
        public void Should_Find_Terms_Random_Non_Ascii_Strings()
        {
            var uniqueStrings = new HashSet<string>();
            var random = new Random(Seed: 3117);

            const int unicodeStart = 0x0080;
            const int unicodeEnd = 0xFFFF;

            while (uniqueStrings.Count < 64 * 1024)
            {
                var length = random.Next(1, 101);

                var chars = new char[length];
                for (var j = 0; j < length; j++)
                {
                    chars[j] = (char)random.Next(unicodeStart, unicodeEnd + 1);
                }

                uniqueStrings.Add(new string(chars));
            }

            var sortedStrings = new List<string>(uniqueStrings);
            sortedStrings.Sort(string.CompareOrdinal);

            var terms = new UnmanagedStringArray(sortedStrings.Count + 1, startIndex: 0);
            foreach (var str in sortedStrings)
            {
                terms.Add(new Span<char>(str.ToCharArray()));
            }

            for (var i = 0; i < terms.Length; i++)
            {
                var position = FieldComparator.BinarySearch(terms, terms[i]);
                Assert.AreEqual(i, position);

                Assert.AreEqual(sortedStrings[i], terms[i].ToString());
            }
        }

        [Test]
        public void Should_Find_Terms_Random_Strings()
        {
            var uniqueStrings = new HashSet<string>();
            var random = new Random(Seed: 3117);

            const int asciiStart = 0x00;
            const int asciiEnd = 0x7F;

            const int unicodeStart = 0x0080;
            const int unicodeEnd = 0xFFFF;

            while (uniqueStrings.Count < 64 * 1024)
            {
                var type = random.Next(3); // 0 = ASCII only, 1 = non-ASCII only, 2 = mixed

                var length = random.Next(1, 101);
                var chars = new char[length];

                for (var j = 0; j < length; j++)
                {
                    switch (type)
                    {
                        case 0:
                            // ascii only
                            chars[j] = (char)random.Next(asciiStart, asciiEnd + 1);
                            break;
                        case 1:
                            // non-ascii only
                            chars[j] = (char)random.Next(unicodeStart, unicodeEnd + 1);
                            break;
                        default:
                        {
                            // randomly pick ascii or non-ascii for this character
                            if (random.Next(2) == 0)
                            {
                                chars[j] = (char)random.Next(asciiStart, asciiEnd + 1);
                            }
                            else
                            {
                                chars[j] = (char)random.Next(unicodeStart, unicodeEnd + 1);
                            }

                            break;
                        }
                    }
                }

                uniqueStrings.Add(new string(chars));
            }

            var sortedStrings = new List<string>(uniqueStrings);
            sortedStrings.Sort(string.CompareOrdinal);

            var terms = new UnmanagedStringArray(sortedStrings.Count + 1, startIndex: 0);
            foreach (var str in sortedStrings)
            {
                terms.Add(new Span<char>(str.ToCharArray()));
            }

            for (var i = 0; i < terms.Length; i++)
            {
                var position = FieldComparator.BinarySearch(terms, terms[i]);
                Assert.AreEqual(i, position);

                Assert.AreEqual(sortedStrings[i], terms[i].ToString());
            }
        }

        [Test]
        public unsafe void Compare_Unmanaged_Strings()
        {
            using (var terms = new UnmanagedStringArray(128, startIndex: 0))
            {
                const string word1 = "";
                terms.Add(word1.ToCharArray());

                var result = UnmanagedString.CompareOrdinal(terms[0], Span<byte>.Empty, Span<char>.Empty);
                Assert.AreEqual(0, result);

                result = UnmanagedString.CompareOrdinal(new UnmanagedString(), Span<byte>.Empty, Span<char>.Empty);
                Assert.AreEqual(-1, result);

                const string word2 = "גרישה";
                terms.Add(word2.ToCharArray());

                // we pass Span<byte>.Empty since we compare by chars and not by bytes
                result = UnmanagedString.CompareOrdinal(terms[1], Span<byte>.Empty, "גרישה");
                Assert.AreEqual(0, result);

                result = UnmanagedString.CompareOrdinal(terms[1], Span<byte>.Empty, "כרמל");
                Assert.True(result < 0);

                result = UnmanagedString.CompareOrdinal(terms[1], Span<byte>.Empty, "אורן");
                Assert.True(result > 0);

                const string word3 = "zebra";
                terms.Add(word3.ToCharArray());

                // we pass Span<char>.Empty since we compare by bytes and not by chars
                var toCompare1 = "גרישה";
                var size = Encoding.UTF8.GetByteCount(toCompare1);
                Span<byte> stringAsBytes = stackalloc byte[size];
                Encoding.UTF8.GetBytes(toCompare1, stringAsBytes);
                result = UnmanagedString.CompareOrdinal(terms[2], stringAsBytes, Span<char>.Empty);
                Assert.True(result < 0);

                var toCompare2 = "כרמל";
                size = Encoding.UTF8.GetByteCount(toCompare2);
                stringAsBytes = stackalloc byte[size];
                Encoding.UTF8.GetBytes(toCompare2, stringAsBytes);
                result = UnmanagedString.CompareOrdinal(terms[2], stringAsBytes, Span<char>.Empty);
                Assert.True(result < 0);

                var toCompare3 = "אורן";
                size = Encoding.UTF8.GetByteCount(toCompare3);
                stringAsBytes = stackalloc byte[size];
                Encoding.UTF8.GetBytes(toCompare3, stringAsBytes);
                result = UnmanagedString.CompareOrdinal(terms[2], stringAsBytes, Span<char>.Empty);
                Assert.True(result < 0);

                size = Encoding.UTF8.GetByteCount(word1);
                stringAsBytes = stackalloc byte[size];
                Encoding.UTF8.GetBytes(word1, stringAsBytes);
                result = UnmanagedString.CompareOrdinal(terms[2], stringAsBytes, Span<char>.Empty);
                Assert.True(result > 0);

                size = Encoding.UTF8.GetByteCount(word2);
                stringAsBytes = stackalloc byte[size];
                Encoding.UTF8.GetBytes(word2, stringAsBytes);
                result = UnmanagedString.CompareOrdinal(terms[2], stringAsBytes, Span<char>.Empty);
                Assert.True(result < 0);

                size = Encoding.UTF8.GetByteCount(word3);
                stringAsBytes = stackalloc byte[size];
                Encoding.UTF8.GetBytes(word3, stringAsBytes);
                result = UnmanagedString.CompareOrdinal(terms[2], stringAsBytes, Span<char>.Empty);
                Assert.AreEqual(0, result);

                result = terms[0].CompareTo(toCompare1);
                Assert.True(result < 0);
                result = terms[0].CompareTo(toCompare2);
                Assert.True(result < 0);
                result = terms[0].CompareTo(toCompare3);
                Assert.True(result < 0);
                result = terms[0].CompareTo(word1);
                Assert.AreEqual(0, result);
                result = terms[0].CompareTo(word2);
                Assert.True(result < 0);
                result = terms[0].CompareTo(word3);
                Assert.True(result < 0);

                result = terms[1].CompareTo(toCompare1);
                Assert.AreEqual(0, result);
                result = terms[1].CompareTo(toCompare2);
                Assert.True(result < 0);
                result = terms[1].CompareTo(toCompare3);
                Assert.True(result > 0);
                result = terms[1].CompareTo(word1);
                Assert.True(result > 0);
                result = terms[1].CompareTo(word2);
                Assert.AreEqual(0, result);
                result = terms[1].CompareTo(word3);
                Assert.True(result > 0);

                result = terms[2].CompareTo(toCompare1);
                Assert.True(result < 0);
                result = terms[2].CompareTo(toCompare2);
                Assert.True(result < 0);
                result = terms[2].CompareTo(toCompare3);
                Assert.True(result < 0);
                result = terms[2].CompareTo(word1);
                Assert.True(result > 0);
                result = terms[2].CompareTo(word2);
                Assert.True(result < 0);
                result = terms[2].CompareTo(word3);
                Assert.AreEqual(0, result);
            }
        }

        private static unsafe void VerifyNonExistingTerms(UnmanagedStringArray terms)
        {
            using (var segment = new Segment(10))
            {
                const string smallerThan = "A";
                const string biggerThan = "z";

                var size = (ushort)Encoding.UTF8.GetByteCount(smallerThan);
                var position = segment.Add(size);
                Encoding.UTF8.GetBytes(smallerThan, new Span<byte>(position + sizeof(ushort), size));
                var smallerUnmanagedString = new UnmanagedString
                {
                    Start = position
                };

                var result = FieldComparator.BinarySearch(terms, smallerUnmanagedString);
                Assert.AreEqual(-2, result);

                size = (ushort)Encoding.UTF8.GetByteCount(biggerThan);
                position = segment.Add(size);
                Encoding.UTF8.GetBytes(biggerThan, new Span<byte>(position + sizeof(ushort), size));
                var biggerUnmanagedString = new UnmanagedString
                {
                    Start = position
                };

                result = FieldComparator.BinarySearch(terms, biggerUnmanagedString);
                Assert.AreEqual(-12, result);
            }
        }
    }
}