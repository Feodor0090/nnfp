using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace curse.NNSocket
{
     public enum OutputFrameType : short
    {
        Shutdown = 0,

        // connection start
        Login = 1,
        Auth = 2,

        // info
        Explore = 3,
        ServerToClientInit = 4,
        ClientToServerInit = 5,

        // transmission
        Eof = 254,
        FilePart = 255,
    }

    public  enum InputFrameType : short
    {
        // connection start
        AuthCheckData = 1,
        AuthFailure = 2,

        // info
        DirectoryContents = 3,
        ServerToClientAccept = 4,
        ClientToServerAccept = 5,
        AccessFailure = 6,
        AcceptFailure = 7,

        // transmission
        Eof = 254,
        FilePart = 255,
    }
}
