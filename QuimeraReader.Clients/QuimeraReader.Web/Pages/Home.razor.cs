using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QuimeraReader.Web.Pages;

public partial class Home
{
    [Parameter]
        [SupplyParameterFromQuery(Name = "q")]
        public string? Q { get; set; }
}

