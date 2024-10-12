using Microsoft.AspNetCore.Mvc;

namespace Queen.Ration.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class TestController : ControllerBase
    {
        [HttpGet(Name = "Test")]
        public string Get()
        {
            return "Hello World.";
        }
    }
}
