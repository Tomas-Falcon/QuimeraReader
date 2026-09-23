using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QuimeraReader.Web.Pages;

public partial class Read
{
    [Parameter]
        public int BookId { get; set; }
}

