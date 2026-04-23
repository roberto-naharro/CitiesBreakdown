using System.Collections.Generic;
using System.Linq;

namespace Breakdown
{
    public class Counts<T>
    {
        public readonly Dictionary<T, uint> Counters = new Dictionary<T, uint>();

        public void Add(T value)
        {
            if (!this.Counters.ContainsKey(value))
            {
                this.Counters[value] = 0;
            }
            this.Counters[value]++;
        }

        public IEnumerable<T> Keys => this.Counters.Keys;

        public int Total
        {
            get
            {
                int sum = 0;
                foreach (uint v in this.Counters.Values) sum += (int)v;
                return sum;
            }
        }

        public override string ToString()
        {
            string result = string.Concat(this.Counters.Take(3).Select(x => $"{x.Key}:{x.Value}").ToArray());
            if (this.Keys.Count() > 3)
            {
                result += "...";
            }
            return result;
        }
    }
}
