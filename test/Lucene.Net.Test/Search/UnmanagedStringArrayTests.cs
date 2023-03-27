using System;
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