/*
 *
 * Licensed to the Apache Software Foundation (ASF) under one
 * or more contributor license agreements.  See the NOTICE file
 * distributed with this work for additional information
 * regarding copyright ownership.  The ASF licenses this file
 * to you under the Apache License, Version 2.0 (the
 * "License"); you may not use this file except in compliance
 * with the License.  You may obtain a copy of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing,
 * software distributed under the License is distributed on an
 * "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
 * KIND, either express or implied.  See the License for the
 * specific language governing permissions and limitations
 * under the License.
 *
*/

using System.Linq;
using System.Reflection;

using Lucene.Net.Test.Util;

namespace Lucene.Net
{
    /// <summary>
    /// Support for junit.framework.TestCase.getName().
    /// {{Lucene.Net-2.9.1}} Move to another location after LUCENENET-266
    /// </summary>
    public class TestCase
    {
        public static string GetName()
        {
            return GetTestCaseName(false);
        }

        public static string GetFullName()
        {
            return GetTestCaseName(true);
        }

        static string GetTestCaseName(bool fullName)
        {
            System.Diagnostics.StackTrace trace = StackTraceHelper.Create();
            var frames = trace.GetFrames();
            for (int i = 0; i < frames.Length; i++)
            {
                System.Reflection.MethodBase method = frames[i].GetMethod();
                object[] testAttrs = method.GetCustomAttributes(typeof(NUnit.Framework.TestAttribute), false).ToArray();
                if (testAttrs != null && testAttrs.Length > 0)
                    if (fullName) return method.DeclaringType.FullName + "." + method.Name;
                    else return method.Name;
            }
            return "GetTestCaseName[UnknownTestMethod]";
        }
    }
}
