using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Ec2Manager.Classes
{
    public static class StringExtensions
    {
        public static string StripColors(this string input)
        {
            return Regex.Replace(input, @"\e\[[\d;]*m", "");
        }
    }
}
