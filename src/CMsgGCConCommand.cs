using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Text;

namespace HGV.Crystalys
{
    [global::ProtoBuf.ProtoContract()]
    public partial class CMsgGCConCommand : global::ProtoBuf.IExtensible
    {
        private global::ProtoBuf.IExtension __pbn__extensionData;
        global::ProtoBuf.IExtension global::ProtoBuf.IExtensible.GetExtensionObject(bool createIfMissing)
            => global::ProtoBuf.Extensible.GetExtensionObject(ref __pbn__extensionData, createIfMissing);

        [global::ProtoBuf.ProtoMember(1)]
        [global::System.ComponentModel.DefaultValue("")]
        public string command
        {
            get => __pbn__command ?? "";
            set => __pbn__command = value;
        }
        public bool ShouldSerializecommand() => __pbn__command != null;
        public void Resetcommand() => __pbn__command = null;
        private string __pbn__command;

    }

    [global::ProtoBuf.ProtoContract(Name=@"EGCSystemMsg", EnumPassthru=true)]
    public enum EGCSystemMsg
    {
      [global::ProtoBuf.ProtoEnum(Name=@"k_EGCMsgConCommand", Value=52)]
      k_EGCMsgConCommand = 52,
    }
}
