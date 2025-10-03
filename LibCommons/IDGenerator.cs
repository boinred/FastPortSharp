using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibCommons
{
    public class IDGenerator
    {
        private long m_Counter = 0; 

        public long GetNextGeneratedId() => Interlocked.Increment(ref m_Counter);

        public Guid GetNextGeneratedGuid() => Guid.NewGuid();
    }
}
