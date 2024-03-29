﻿namespace SlimCluster.Membership.Swim;

public class SwimMemberStatus : AbstractStatus<SwimMemberStatus>, INodeStatus
{
    public bool IsActive { get; }

    protected SwimMemberStatus(Guid id, string name, bool isActive) : base(id, name)
    {
        IsActive = isActive;
    }

    public static readonly SwimMemberStatus Active = new(new Guid("{112D19E0-A0B3-4EDD-8A35-76F299204406}"), "Active", true);
    public static readonly SwimMemberStatus Confirming = new(new Guid("{0BB58A7C-8ABF-4125-BFE4-C7D9BF62F8D0}"), "Confirming", true);
    public static readonly SwimMemberStatus Suspicious = new(new Guid("{98D19B73-1CCD-43D5-B3AC-CFFCD2A9305B}"), "Suspicious", false);
    public static readonly SwimMemberStatus Faulted = new(new Guid("{529CD1AF-A1F7-449D-AC17-EE162B8F78B6}"), "Faulted", false);
}
