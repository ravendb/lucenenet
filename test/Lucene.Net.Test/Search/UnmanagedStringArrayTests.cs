using System;
using System.Collections.Generic;
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
            var terms = new UnmanagedStringArray(11);

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
            var terms = new UnmanagedStringArray(11);

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

            terms = new UnmanagedStringArray(11);

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

            terms = new UnmanagedStringArray(11);

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
            var terms = new UnmanagedStringArray(char.MaxValue + 1);
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

            var terms = new UnmanagedStringArray(sortedStrings.Count + 1);
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

            var terms = new UnmanagedStringArray(sortedStrings.Count + 1);
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

            var terms = new UnmanagedStringArray(sortedStrings.Count + 1);
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

        private static unsafe void VerifyNonExistingTerms(UnmanagedStringArray terms)
        {
            using (var segment = new Segment(10))
            {
                const string smallerThan = "A";
                const string biggerThan = "z";

                var size = (ushort)Encoding.UTF8.GetByteCount(smallerThan);
                segment.Add(size, out var position);
                Encoding.UTF8.GetBytes(smallerThan, new Span<byte>(position + sizeof(ushort), size));
                var smallerUnmanagedString = new UnmanagedString
                {
                    Start = position
                };

                var result = FieldComparator.BinarySearch(terms, smallerUnmanagedString);
                Assert.AreEqual(-2, result);

                size = (ushort)Encoding.UTF8.GetByteCount(biggerThan);
                segment.Add(size, out position);
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