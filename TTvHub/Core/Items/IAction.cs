using Lua;

namespace TTvHub.Core.Items
{
    public interface IAction
    {
        public LuaFunction Function { get; set; }
    }
}
