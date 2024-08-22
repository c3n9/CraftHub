using CraftHub.ViewModels.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CraftHub.ViewModels
{
	internal class ChatWithBotViewModel : BaseViewModel
	{
		private string _apiKey;
		public string ApiKey
		{
			get
			{
				return _apiKey;
			}
			set
			{
				_apiKey = value;
				OnPropertyChanged();
			}
		}

		private string _stringRequest;
		public string StringRequest
		{
			get
			{
				return _stringRequest;
			}
			set
			{
				_stringRequest = value;
				OnPropertyChanged();
			}
		}

		private string _stringResponse;
		public string StringResponse
		{
			get
			{
				return _stringResponse;
			}
			set
			{
				_stringResponse = value;
				OnPropertyChanged();
			}
		}

		public ChatWithBotViewModel()
        {
            
        }
    }
}
