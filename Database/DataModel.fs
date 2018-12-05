module monoid.DataModel

module User =
    type Id = int

    type UserBasicInfo =
        {
            id: Id
            fullName: string
        }
    
    type GitHubUser =
        {
            login: string
            name: string
        }

    type VkUser =
        {
            id: int
            first_name: string
            last_name: string
        }

    type ProviderUser = 
        GitHubProviderUser of GitHubUser 
        | VkProviderUser of VkUser

module Problem =
    open System

    type ProblemDescriptionExample = {
        input: string
        output: string
    }

    type Problem = {
        problem: string
        inputFormat: string
        outputFormat: string
        examples: ProblemDescriptionExample list
    }

    type ProblemDescription = {
        id: int option
        title: string
        description : Problem
    }

    type ProblemHeadline = {
        id: int
        title: string
    }
    
    type Conciseness = {
        tokenCount: int
        literalLength: int
    }

    type SolutionSummary = {
        user: int
        solution: int
        problem: int
        submitDate: DateTime
        verdict: string
        failedTest: int option
        error: string option                
        memory: int option
        conciseness: Conciseness
    }

    type Solution = {
        summary : SolutionSummary
        source : string
    }

    type Test = {
        id: int
        input: string
        output: string
    }

    type SolutionToCheck = {
        tests: Test []
        time: int64
        memory: int64
        source: string
        id: int
    }

    type HallOfFameRaw = {
        user: int
        problem: int
        conciseness: Conciseness
    }

module Solution =
    type State = Submitted | Checking | Checked