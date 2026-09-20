namespace Gopet.IO
{
    public class MsgReader
    {

        protected Session session;
        private long idleTime = 0L;
        public MsgReader(Session session)
        {
            this.session = session;
        }

        public void run()
        {
            while (true)
            {
                try
                {
                    if (session.isConnected())
                    {
                        Message message = readMessage();
                        if (message != null)
                        {
                            session.messageHandler.onMessage(message);
                            session.msgCount++;
                            continue;
                        }
                    }
                }
                catch (Exception var5)
                {
                    session.Close();
#if DEBUG
                    //throw var5;
#endif
                }

                if (session.isConnected())
                {
                    if (session.messageHandler != null)
                    {
                        session.messageHandler.onDisconnected();
                    }
                    session.Close();
                }
                return;
            }
        }

        private Message readMessage()
        {
            int Length = 0;
            int hi = session.dis.ReadJavaInt();

            if (hi == -1)
            {
                return null;
            }
            else
            {
                Length = hi - 1;
                if (Length > 10000)
                {
                    throw new IOException("Dữ liệu quá lớn");
                }
                sbyte isEncrypted = session.dis.ReadSByte();
                // Bắt buộc mã hoá: gói cờ 0 (tool/bot gửi thô) bị từ chối, đóng kết nối.
                if (isEncrypted != 1)
                {
                    throw new IOException("Gói không mã hoá");
                }
                byte[] data = new byte[Length];
                int len = 0;
                int sbyteRead = 0;

                while (len != -1 && sbyteRead < Length)
                {
                    len = session.dis.Read(data, sbyteRead, Length - sbyteRead);
                    if (len > 0)
                    {
                        sbyteRead += len;
                    }
                }

                if (Length == 0)
                {
                    return null;
                }
                else
                {
                    Message msg;
                    if (isEncrypted == 1)
                    {
                        sbyte[] plain = session.tea.decrypt(data.sbytes());
                        if (plain == null || plain.Length == 0)
                        {
                            throw new IOException("Giải mã thất bại");
                        }
                        msg = new Message(plain);
                    }
                    else
                    {
                        msg = new Message(data.sbytes());
                    }
                    Session var10000 = session;
                    var10000.recvsbyteCount += 4 + Length;
                    return msg;
                }
            }
        }

    }
}