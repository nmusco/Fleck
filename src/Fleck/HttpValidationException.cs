using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Fleck
{
    public class HttpRewuestMessageValidationException : System.Exception
    {
        public HttpRequestMessageValidationExceptionTypes Code { get; set; }

        public static string GetHttpRequestMessageValidationExceptionTypeMessage(HttpRequestMessageValidationExceptionTypes code)
        {
            switch (code)
            {
                default:
                    return "Unknown validation error.";
            }
        }

        public HttpRewuestMessageValidationException(HttpRequestMessageValidationExceptionTypes code)
            : base(GetHttpRequestMessageValidationExceptionTypeMessage(code))
        {
            this.Code = code;
        }
    }
}
