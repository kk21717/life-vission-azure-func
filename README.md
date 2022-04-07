Introduction

	'Rent Ready Technical Assessment' is the title of an assignment introduced by Life Vission Recruitment Agency. It is based on performing simple crud operations on a Dynamic 365 dataverse. It consists tow tasks, task 1 which is setting up the dataverse environment, and the second is to implement	an azure function to receive a date range as payload and insert every date in between into a dataverse table named 'Time Entry'. Every single day in the range should be inserted in dataverse	leading to a time entry row having the 'start' and 'end' fields with the same ( value of that day ).


Assumptions:

	1. The azure function uses a HTTP trigger. 

	2. if the dataverse table with the expected name does not exists on environment, it will be created by the code 
            table name: kk_timeentry
			table fields: kk_title (primary string column), kk_start (DateOnly), kk_end (DateOnly)
			the field names are set as constant strings in TimeEntryFunction.cs

	3. The implementation is not developed/tuned for efficient performance in scaled environments


Run locally (using visual studio)

	1. Clone the repo to your local development system

	2. Set your dataverse connection string into the key 'MyDataverseConnection' in TimeEntryManager\local.settings.json file
		* example for local.settings.json:
		{
  			"MyDataverseConnection": "AuthType=OAuth;Username = <Your Dataverse Account Username>;Password = <Your Dataverse Account Password>;Url = <Your Dataverse Environment Url>;"
		}

	3. Press (F5) to build, run and execute the TimeEntryManager function locally


Test locally (using visual studio)

	1. Steps 1 and 2 from the previous section (clone repo and set the connection string)

	2. Open 'Test Explorer' and press the 'Run All Tests' button


Deployment to Azure cloud

	1. Your dataverse connection string into the key 'MyDataverseConnection' in Environment Variables 
		* You can simply define variables in GitHub or azure devops, ...
		* Setting via environment variable can also be used locally, it will override the value in local.settings.json 
		* Example for an environment variable:
			"MyDataverseConnection" = "AuthType=OAuth;Username = <Your Dataverse Account Username>;Password = <Your Dataverse Account Password>;Url = <Your Dataverse Environment Url>;"

	2. Set variables in azurepiplines.yml, at the top section of the file (variables), assign value to:
		subscriptionName: <Name of your Azure subscription>
		appName: <Name of the function app>

	3. Run the pipeline manually or push on the master branch and it will automatically trigger the pipeline that builds, test, and deploy your code to the specified azure function app.


