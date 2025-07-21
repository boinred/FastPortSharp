namespace LibCommonTest
{
    [TestClass]
    public sealed class BaseCircularBufferTest
    {
        [TestMethod]
        public void TestWriteBuffers()
        {
            byte[] writeBuffers = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
            
            var circularBuffer = new LibCommons.BaseCircularBuffers(10);
            circularBuffer.Write(writeBuffers, 0, 6);
            circularBuffer.Write(writeBuffers, 6, 4);
            

            var buffers = new byte[10];
            circularBuffer.Peek(ref buffers);
            Assert.AreEqual(buffers[0], writeBuffers[0]);
            Assert.AreEqual(buffers[1], writeBuffers[1]);
            Assert.AreEqual(buffers[2], writeBuffers[2]);
            Assert.AreEqual(buffers[3], writeBuffers[3]);
            Assert.AreEqual(buffers[4], writeBuffers[4]);
            Assert.AreEqual(buffers[5], writeBuffers[5]);
            Assert.AreEqual(buffers[6], writeBuffers[6]);
            Assert.AreEqual(buffers[7], writeBuffers[7]);
            Assert.AreEqual(buffers[8], writeBuffers[8]);
            Assert.AreEqual(buffers[9], writeBuffers[9]);
        }

        [TestMethod]
        public void TestWriteBuffersCircular()
        {
            byte[] writeBuffers = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

            var circularBuffer = new LibCommons.BaseCircularBuffers(10);

            // [0, 1, 2, 3, 4, 5, 6]
            circularBuffer.Write(writeBuffers, 0, 6);
            // [..7, 8, 9, 10]
            circularBuffer.Write(writeBuffers, 6, 4);


            var buffers = new byte[10];
            var readBufferSize = circularBuffer.Peek(ref buffers);
            circularBuffer.Drain(readBufferSize); 

            Assert.AreEqual(buffers[0], writeBuffers[0]);
            Assert.AreEqual(buffers[1], writeBuffers[1]);
            Assert.AreEqual(buffers[2], writeBuffers[2]);
            Assert.AreEqual(buffers[3], writeBuffers[3]);
            Assert.AreEqual(buffers[4], writeBuffers[4]);
            Assert.AreEqual(buffers[5], writeBuffers[5]);
            Assert.AreEqual(buffers[6], writeBuffers[6]);
            Assert.AreEqual(buffers[7], writeBuffers[7]);
            Assert.AreEqual(buffers[8], writeBuffers[8]);
            Assert.AreEqual(buffers[9], writeBuffers[9]);

            

            // [9, 10, 2, 3, 4, 5, 6, 7 ,8 ,9, 10]
            circularBuffer.Write(writeBuffers, 8, 2);

            var buffers2 = new byte[10];
            circularBuffer.Peek(ref buffers2);

            Assert.AreEqual(buffers2[0], 9);
            Assert.AreEqual(buffers2[1], 10);

        }
    }
}
